using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability
{
    internal sealed partial class PortabilityPackageInspector
    {
        private const string StagingPrefix = ".home-ledger-portability-staging-";
        private const string MarkerFileName = ".home-ledger-staging-identity";
        private readonly PortabilityContractCatalog _catalog;
        private readonly PortabilityPackageInspectionOptions _options;

        public PortabilityPackageInspector(PortabilityPackageInspectionOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
            _catalog = PortabilityContractCatalog.Load();
        }

        public async Task InspectAndExtractAsync(
            Stream archiveStream,
            string extractionRoot,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(archiveStream);
            cancellationToken.ThrowIfCancellationRequested();
            if (!archiveStream.CanRead)
            {
                throw new ArgumentException("The portability archive stream must be readable.", nameof(archiveStream));
            }

            var root = ValidateEmptyExtractionRoot(extractionRoot);
            var staging = CreateStagingDirectory(root);
            var promoted = new List<string>();

            try
            {
                var prepared = await PrepareArchiveStreamAsync(archiveStream, staging.Path, cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    using var archive = new ZipArchive(prepared.Stream, ZipArchiveMode.Read, true);
                    cancellationToken.ThrowIfCancellationRequested();
                    var entries = InspectCentralDirectory(archive, cancellationToken);
                    var extracted = await ExtractEntriesAsync(
                        entries,
                        Path.Combine(staging.Path, "content"),
                        cancellationToken).ConfigureAwait(false);
                    await ValidateExtractedPackageAsync(extracted, staging.Path, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (prepared.OwnsStream)
                    {
                        await prepared.Stream.DisposeAsync().ConfigureAwait(false);
                    }
                }

                Promote(staging, root, promoted);
                DeleteVerifiedStaging(staging, root);
            }
            catch (Exception exception)
            {
                RemovePromoted(promoted, root);
                CleanupOrQuarantine(staging, root, exception);
                if (exception is OperationCanceledException or PortabilityPackageValidationException)
                {
                    throw;
                }

                throw PortabilityPackageValidationException.Create("archive.invalid", innerException: exception);
            }
        }

        private async Task<PreparedArchiveStream> PrepareArchiveStreamAsync(
            Stream source,
            string stagingPath,
            CancellationToken cancellationToken)
        {
            if (source.CanSeek)
            {
                if (source.Length > _options.MaxArchiveCompressedBytes)
                {
                    throw Reject("archive.compressed-size-limit");
                }

                source.Position = 0;
                return new PreparedArchiveStream(source, false);
            }

            var path = Path.Combine(stagingPath, "archive.zip");
            await using var destination = OpenNewPrivateFile(path);
            var buffer = new byte[_options.BufferSizeBytes];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > _options.MaxArchiveCompressedBytes)
                {
                    throw Reject("archive.compressed-size-limit");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            var staged = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _options.BufferSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return new PreparedArchiveStream(staged, true);
        }

        private static string ValidateEmptyExtractionRoot(string extractionRoot)
        {
            if (string.IsNullOrWhiteSpace(extractionRoot))
            {
                throw new ArgumentException("An extraction root is required.", nameof(extractionRoot));
            }

            var root = Path.GetFullPath(extractionRoot);
            if (!Directory.Exists(root)
                || Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar) == root.TrimEnd(Path.DirectorySeparatorChar)
                || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw Reject("archive.target-invalid");
            }

            if (Directory.EnumerateFileSystemEntries(root).Any())
            {
                throw Reject("archive.target-not-empty");
            }

            return root;
        }

        private static StagingIdentity CreateStagingDirectory(string root)
        {
            var identity = Guid.NewGuid().ToString("N");
            var path = Path.Combine(root, $"{StagingPrefix}{identity}");
            Directory.CreateDirectory(path);
            ApplyPrivateDirectoryPermissions(path);
            var marker = Path.Combine(path, MarkerFileName);
            File.WriteAllText(marker, identity, new UTF8Encoding(false));
            ApplyPrivateFilePermissions(marker);
            return new StagingIdentity(path, identity);
        }

        private static void Promote(StagingIdentity staging, string root, List<string> promoted)
        {
            var content = Path.Combine(staging.Path, "content");
            foreach (var source in Directory.EnumerateFileSystemEntries(content))
            {
                var destination = Path.Combine(root, Path.GetFileName(source));
                if (Directory.Exists(source))
                {
                    Directory.Move(source, destination);
                }
                else
                {
                    File.Move(source, destination);
                }

                promoted.Add(destination);
            }
        }

        private static void RemovePromoted(IEnumerable<string> promoted, string root)
        {
            foreach (var path in promoted.Reverse())
            {
                if (!IsDirectChild(path, root))
                {
                    throw new InvalidOperationException("Refusing to clean an unverified promoted package path.");
                }

                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static void CleanupOrQuarantine(
            StagingIdentity staging,
            string root,
            Exception originalException)
        {
            try
            {
                DeleteVerifiedStaging(staging, root);
            }
            catch (Exception cleanupException)
            {
                TryQuarantine(staging, root);
                throw PortabilityPackageValidationException.Create(
                    "archive.cleanup-failed",
                    innerException: new AggregateException(originalException, cleanupException));
            }
        }

        private static void DeleteVerifiedStaging(StagingIdentity staging, string root)
        {
            if (!Directory.Exists(staging.Path))
            {
                return;
            }

            var marker = Path.Combine(staging.Path, MarkerFileName);
            if (!IsDirectChild(staging.Path, root)
                || !Path.GetFileName(staging.Path).StartsWith(StagingPrefix, StringComparison.Ordinal)
                || (File.GetAttributes(staging.Path) & FileAttributes.ReparsePoint) != 0
                || !File.Exists(marker)
                || !string.Equals(File.ReadAllText(marker), staging.Identity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to remove an unverified portability staging directory.");
            }

            Directory.Delete(staging.Path, true);
        }

        private static void TryQuarantine(StagingIdentity staging, string root)
        {
            if (!Directory.Exists(staging.Path) || !IsDirectChild(staging.Path, root))
            {
                return;
            }

            var quarantine = Path.Combine(root, $".home-ledger-portability-quarantine-{staging.Identity}");
            try
            {
                Directory.Move(staging.Path, quarantine);
            }
            catch (IOException)
            {
                // The protected staging directory remains visible for operator cleanup.
            }
        }

        private static bool IsDirectChild(string path, string root) =>
            string.Equals(
                Directory.GetParent(Path.GetFullPath(path))?.FullName,
                Path.GetFullPath(root),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        private static FileStream OpenNewPrivateFile(string path)
        {
            var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ApplyPrivateFilePermissions(path);
            return stream;
        }

        private static void ApplyPrivateDirectoryPermissions(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                var identity = GetWindowsIdentity();
                var security = new DirectorySecurity();
                security.SetOwner(identity);
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(
                    new FileSystemAccessRule(
                        identity,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                new DirectoryInfo(path).SetAccessControl(security);
                return;
            }

            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        private static void ApplyPrivateFilePermissions(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                var identity = GetWindowsIdentity();
                var security = new FileSecurity();
                security.SetOwner(identity);
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(
                    new FileSystemAccessRule(
                        identity,
                        FileSystemRights.FullControl,
                        AccessControlType.Allow));
                new FileInfo(path).SetAccessControl(security);
                return;
            }

            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        private static SecurityIdentifier GetWindowsIdentity() =>
            WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("A Windows security identity is required for protected staging.");

        private static PortabilityPackageValidationException Reject(string code, string entry = null) =>
            PortabilityPackageValidationException.Create(code, entry);

        private sealed record StagingIdentity(string Path, string Identity);

        private sealed record PreparedArchiveStream(Stream Stream, bool OwnsStream);
    }
}
