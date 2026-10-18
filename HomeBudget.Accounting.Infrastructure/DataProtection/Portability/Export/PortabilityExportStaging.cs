using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    internal sealed class PortabilityExportStaging : IDisposable
    {
        private const string Prefix = ".home-ledger-export-staging-";
        private const string MarkerName = ".home-ledger-export-staging-identity";
        private readonly string _parent;
        private readonly string _identity;

        public PortabilityExportStaging(string destinationPath)
        {
            DestinationPath = Path.GetFullPath(destinationPath);
            _parent = Path.GetDirectoryName(DestinationPath)
                ?? throw new PortabilityExportValidationException("export.destination-invalid");
            if (!Directory.Exists(_parent)
                || (File.GetAttributes(_parent) & FileAttributes.ReparsePoint) != 0
                || File.Exists(DestinationPath))
            {
                throw new PortabilityExportValidationException("export.destination-invalid");
            }

            _identity = Guid.NewGuid().ToString("N");
            RootPath = Path.Combine(_parent, $"{Prefix}{_identity}");
            ContentPath = Path.Combine(RootPath, "content");
            ArchivePath = Path.Combine(RootPath, "archive.zip");
            try
            {
                Directory.CreateDirectory(ContentPath);
                ProtectDirectory(RootPath);
                File.WriteAllText(Path.Combine(RootPath, MarkerName), _identity);
            }
            catch
            {
                if (Directory.Exists(RootPath) && IsDirectChild(RootPath, _parent))
                {
                    Directory.Delete(RootPath, true);
                }

                throw;
            }
        }

        public string DestinationPath { get; }

        public string RootPath { get; }

        public string ContentPath { get; }

        public string ArchivePath { get; }

        public string GetContentPath(string relativePath)
        {
            var path = Path.GetFullPath(Path.Combine(ContentPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(ContentPath + Path.DirectorySeparatorChar, Comparison))
            {
                throw new PortabilityExportValidationException("export.path-invalid");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

        public void Publish()
        {
            File.Move(ArchivePath, DestinationPath, false);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath) && IsVerified())
            {
                Directory.Delete(RootPath, true);
            }
        }

        private static StringComparison Comparison => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        private static void ProtectDirectory(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return;
            }

            var identity = WindowsIdentity.GetCurrent().User
                ?? throw new PortabilityExportValidationException("export.temp-permissions");
            var security = new DirectorySecurity();
            security.SetOwner(identity);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }

        private bool IsVerified()
        {
            var marker = Path.Combine(RootPath, MarkerName);
            return IsDirectChild(RootPath, _parent)
                && Path.GetFileName(RootPath).StartsWith(Prefix, StringComparison.Ordinal)
                && (File.GetAttributes(RootPath) & FileAttributes.ReparsePoint) == 0
                && File.Exists(marker)
                && string.Equals(File.ReadAllText(marker), _identity, StringComparison.Ordinal);
        }

        private static bool IsDirectChild(string path, string parent) => string.Equals(
            Directory.GetParent(Path.GetFullPath(path))?.FullName,
            Path.GetFullPath(parent),
            Comparison);
    }
}
