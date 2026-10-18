using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts
{
    internal sealed partial class DisposableFileSystemArtifactStore
    {
        private string PathFor(params string[] segments)
        {
            var path = _rootPath;
            foreach (var segment in segments)
            {
                path = Path.Combine(path, segment);
            }

            var resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw Failure("artifact-path-invalid", "Artifact path is outside the verified root.");
            }

            return resolved;
        }

        private string ContentPath(string area, string sha256) =>
            PathFor(area, "sha256", sha256[..2], sha256);

        private string CreateTemporaryPath(string area)
        {
            var directory = PathFor(area);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        }

        private static FileStream OpenNewWrite(string path) => new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        private static async Task<FileHashEvidence> HashFileAsync(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            long length = 0;
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    length += read;
                }

                return new FileHashEvidence(
                    length,
                    Convert.ToHexString(hash.GetHashAndReset()));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static void CommitContentAddressed(
            string temporaryPath,
            string destination,
            FileHashEvidence evidence)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try
            {
                File.Move(temporaryPath, destination, false);
                File.SetAttributes(destination, FileAttributes.ReadOnly);
            }
            catch (IOException) when (File.Exists(destination))
            {
                if (IsReparsePoint(destination))
                {
                    throw Failure("content-address-conflict", "Content-addressed artifact conflicts with existing data.");
                }

                var existing = new FileInfo(destination);
                using var stream = File.OpenRead(destination);
                var existingHash = Convert.ToHexString(SHA256.HashData(stream));
                if (existing.Length != evidence.Size ||
                    !string.Equals(existingHash, evidence.Sha256, StringComparison.Ordinal))
                {
                    throw Failure("content-address-conflict", "Content-addressed artifact conflicts with existing data.");
                }
            }
        }

        private static void CommitManifest(string temporaryPath, string destination, string expectedHash)
        {
            try
            {
                File.Move(temporaryPath, destination, false);
                File.SetAttributes(destination, FileAttributes.ReadOnly);
            }
            catch (IOException) when (File.Exists(destination))
            {
                if (IsReparsePoint(destination))
                {
                    throw Failure("manifest-identity-conflict", "Recovery-set manifest identity conflicts.");
                }

                using var stream = File.OpenRead(destination);
                var existingHash = Convert.ToHexString(SHA256.HashData(stream));
                if (!string.Equals(existingHash, expectedHash, StringComparison.Ordinal))
                {
                    throw Failure("manifest-identity-conflict", "Recovery-set manifest identity conflicts.");
                }
            }
        }

        private static Task<Stream> OpenReadAsyncCore(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path) || IsReparsePoint(path))
            {
                throw Failure("protected-artifact-unavailable", "Protected artifact is unavailable.");
            }

            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }

        private void WriteQuarantineMarker(Guid recoverySetId, string reasonCode)
        {
            var directory = PathFor("quarantine", recoverySetId.ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "reason.txt"), SanitizeReason(reasonCode));
        }

        private static string SanitizeReason(string reasonCode) =>
            !string.IsNullOrWhiteSpace(reasonCode) && reasonCode.Length <= 128 &&
            Array.TrueForAll(reasonCode.ToCharArray(), character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
                ? reasonCode
                : "artifact-failure";

        private static void SecureDeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (IsReparsePoint(file))
                {
                    throw Failure("plaintext-cleanup-failed", "Plaintext cleanup encountered an unsafe link.");
                }

                File.SetAttributes(file, FileAttributes.Normal);
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var zeros = new byte[64 * 1024];
                    long remaining = stream.Length;
                    while (remaining > 0)
                    {
                        var count = (int)Math.Min(zeros.Length, remaining);
                        stream.Write(zeros, 0, count);
                        remaining -= count;
                    }

                    stream.Flush(true);
                }

                File.Delete(file);
            }

            Directory.Delete(path, true);
        }

        private static void DeleteTemporaryFile(string path)
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }

        private static string Sha256(ReadOnlySpan<byte> content) =>
            Convert.ToHexString(SHA256.HashData(content));

        private sealed record FileHashEvidence(long Size, string Sha256);
    }
}
