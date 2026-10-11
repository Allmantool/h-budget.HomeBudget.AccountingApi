using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability
{
    internal sealed partial class PortabilityPackageInspector
    {
        private static readonly string[] NestedArchiveExtensions =
        [
            ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz", ".bz2", ".xz"
        ];

        private static readonly string[] ExecutableExtensions =
        [
            ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".sh", ".msi", ".jar"
        ];

        private static readonly byte[] ZipLocalHeader = [0x50, 0x4b, 0x03, 0x04];
        private static readonly byte[] ZipEmptyHeader = [0x50, 0x4b, 0x05, 0x06];
        private static readonly byte[] GzipHeader = [0x1f, 0x8b];
        private static readonly byte[] SevenZipHeader = [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c];
        private static readonly byte[] RarHeader = [0x52, 0x61, 0x72, 0x21];

        private List<InspectedArchiveEntry> InspectCentralDirectory(
            ZipArchive archive,
            CancellationToken cancellationToken)
        {
            if (archive.Entries.Count > _options.MaxEntryCount)
            {
                throw Reject("archive.entry-count-limit");
            }

            var exact = new HashSet<string>(StringComparer.Ordinal);
            var caseInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inspected = new List<InspectedArchiveEntry>(archive.Entries.Count);
            long totalUncompressed = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ValidateEntryPath(entry.FullName);
                ValidateUniquePath(path, exact, caseInsensitive);
                var rule = ValidateEntryType(entry, path);
                totalUncompressed = ValidateEntryLimits(entry, rule, totalUncompressed);

                inspected.Add(new InspectedArchiveEntry(entry, rule));
            }

            return inspected;
        }

        private async Task<ExtractedPackage> ExtractEntriesAsync(
            IEnumerable<InspectedArchiveEntry> entries,
            string contentRoot,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(contentRoot);
            ApplyPrivateDirectoryPermissions(contentRoot);
            var extracted = new Dictionary<string, ExtractedEntry>(StringComparer.Ordinal);
            long total = 0;

            foreach (var inspected in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ResolveDestination(contentRoot, inspected.Rule.Path);
                CreatePrivateParentDirectories(contentRoot, destination);
                var value = await ExtractEntryAsync(inspected, destination, cancellationToken)
                    .ConfigureAwait(false);
                total = checked(total + value.Bytes);
                if (total > _options.MaxTotalUncompressedBytes)
                {
                    throw Reject("archive.uncompressed-size-limit");
                }

                extracted.Add(inspected.Rule.Path, value);
            }

            return new ExtractedPackage(contentRoot, extracted);
        }

        private static void ValidateUniquePath(
            string path,
            HashSet<string> exact,
            HashSet<string> caseInsensitive)
        {
            if (!exact.Add(path))
            {
                throw Reject("archive.duplicate-entry");
            }

            if (!caseInsensitive.Add(path))
            {
                throw Reject("archive.case-collision");
            }
        }

        private PortabilityCatalogEntry ValidateEntryType(ZipArchiveEntry entry, string path)
        {
            if (IsLink(entry))
            {
                throw Reject("archive.unsafe-link");
            }

            if (path.EndsWith('/'))
            {
                throw Reject("archive.unsupported-entry-type");
            }

            var extension = Path.GetExtension(path);
            if (NestedArchiveExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw Reject("archive.nested-archive");
            }

            if (ExecutableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw Reject("archive.unsupported-entry-type");
            }

            return _catalog.Entries.TryGetValue(path, out var rule)
                ? rule
                : throw Reject("archive.unsupported-entry");
        }

        private long ValidateEntryLimits(
            ZipArchiveEntry entry,
            PortabilityCatalogEntry rule,
            long currentUncompressed)
        {
            if (entry.CompressedLength > _options.MaxEntryCompressedBytes)
            {
                throw Reject("archive.entry-compressed-size-limit", rule.Path);
            }

            if (entry.Length > _options.MaxEntryUncompressedBytes)
            {
                throw Reject("archive.entry-uncompressed-size-limit", rule.Path);
            }

            long total;
            try
            {
                total = checked(currentUncompressed + entry.Length);
            }
            catch (OverflowException exception)
            {
                throw PortabilityPackageValidationException.Create(
                    "archive.uncompressed-size-limit",
                    innerException: exception);
            }

            if (total > _options.MaxTotalUncompressedBytes)
            {
                throw Reject("archive.uncompressed-size-limit");
            }

            var ratio = entry.CompressedLength == 0
                ? entry.Length == 0 ? 1 : double.PositiveInfinity
                : (double)entry.Length / entry.CompressedLength;
            if (ratio > _options.MaxExpansionRatio)
            {
                throw Reject("archive.expansion-ratio-limit", rule.Path);
            }

            return total;
        }

        private async Task<ExtractedEntry> ExtractEntryAsync(
            InspectedArchiveEntry inspected,
            string destination,
            CancellationToken cancellationToken)
        {
            await using var output = OpenNewPrivateFile(destination);
            await using var input = await inspected.Entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[_options.BufferSizeBytes];
            var signature = new byte[8];
            var signatureLength = 0;
            long entryBytes = 0;

            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                entryBytes = checked(entryBytes + read);
                if (entryBytes > _options.MaxEntryUncompressedBytes)
                {
                    throw Reject("archive.entry-uncompressed-size-limit", inspected.Rule.Path);
                }

                signatureLength = CaptureSignature(buffer, read, signature, signatureLength);
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            if (entryBytes != inspected.Entry.Length)
            {
                throw Reject("archive.entry-size-mismatch", inspected.Rule.Path);
            }

            if (HasNestedArchiveSignature(signature.AsSpan(0, signatureLength)))
            {
                throw Reject("archive.nested-archive", inspected.Rule.Path);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new ExtractedEntry(
                inspected.Rule,
                destination,
                entryBytes,
                Convert.ToHexStringLower(hash.GetHashAndReset()));
        }

        private static int CaptureSignature(byte[] buffer, int read, byte[] signature, int currentLength)
        {
            if (currentLength >= signature.Length)
            {
                return currentLength;
            }

            var copy = Math.Min(signature.Length - currentLength, read);
            buffer.AsSpan(0, copy).CopyTo(signature.AsSpan(currentLength));
            return currentLength + copy;
        }

        private static string ValidateEntryPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)
                || rawPath.Contains('\0', StringComparison.Ordinal))
            {
                throw Reject("archive.unsupported-entry");
            }

            var decoded = rawPath;
            for (var index = 0; index < 4; index++)
            {
                string next;
                try
                {
                    next = Uri.UnescapeDataString(decoded);
                }
                catch (UriFormatException exception)
                {
                    throw PortabilityPackageValidationException.Create(
                        "archive.path-traversal",
                        innerException: exception);
                }

                if (string.Equals(next, decoded, StringComparison.Ordinal))
                {
                    break;
                }

                decoded = next;
            }

            if (ContainsTraversal(rawPath) || ContainsTraversal(decoded))
            {
                throw Reject("archive.path-traversal");
            }

            if (IsAbsolutePath(rawPath) || IsAbsolutePath(decoded))
            {
                throw Reject("archive.absolute-path");
            }

            if (rawPath.Contains(':', StringComparison.Ordinal)
                || decoded.Contains(':', StringComparison.Ordinal))
            {
                throw Reject("archive.alternate-stream");
            }

            return rawPath.Replace('\\', '/');
        }

        private static bool ContainsTraversal(string path) => path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.None)
            .Any(segment => segment is "." or "..");

        private static bool IsAbsolutePath(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.StartsWith('/')
                || (normalized.Length >= 3
                    && char.IsAsciiLetter(normalized[0])
                    && normalized[1] == ':'
                    && normalized[2] == '/');
        }

        private static bool IsLink(ZipArchiveEntry entry)
        {
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            return unixType == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
        }

        private static bool HasNestedArchiveSignature(ReadOnlySpan<byte> value) =>
            value.StartsWith(ZipLocalHeader)
            || value.StartsWith(ZipEmptyHeader)
            || value.StartsWith(GzipHeader)
            || value.StartsWith(SevenZipHeader)
            || value.StartsWith(RarHeader);

        private static string ResolveDestination(string root, string path)
        {
            var destination = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(
                    prefix,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw Reject("archive.path-traversal");
            }

            return destination;
        }

        private static void CreatePrivateParentDirectories(string root, string destination)
        {
            var relative = Path.GetRelativePath(root, Path.GetDirectoryName(destination));
            var current = root;
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current))
                {
                    Directory.CreateDirectory(current);
                    ApplyPrivateDirectoryPermissions(current);
                }
            }
        }

        private sealed record InspectedArchiveEntry(
            ZipArchiveEntry Entry,
            PortabilityCatalogEntry Rule);

        private sealed record ExtractedPackage(
            string ContentRoot,
            IReadOnlyDictionary<string, ExtractedEntry> Entries);

        private sealed record ExtractedEntry(
            PortabilityCatalogEntry Rule,
            string FilePath,
            long Bytes,
            string Sha256);
    }
}
