using System;
using System.IO;
using System.Text;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability.Export
{
    internal sealed class DisposablePortabilityExportRoot : IDisposable
    {
        private const string MarkerFileName = ".home-ledger-disposable-export-root";
        private readonly string _identity = Guid.NewGuid().ToString("N");
        private bool _disposed;

        public DisposablePortabilityExportRoot()
        {
            RootPath = Path.GetFullPath(
                Directory.CreateTempSubdirectory("home-ledger-portability-export-").FullName);
            MarkerPath = Path.Combine(RootPath, MarkerFileName);
            File.WriteAllText(MarkerPath, _identity, new UTF8Encoding(false));
            DestinationPath = Path.Combine(RootPath, "home-ledger-export.zip");
        }

        public string RootPath { get; }

        public string DestinationPath { get; }

        private string MarkerPath { get; }

        public void AssertNoPublishedOrStagedArtifact()
        {
            Directory.EnumerateFileSystemEntries(RootPath)
                .Should().Equal(MarkerPath);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            VerifyDisposableIdentity();
            Directory.Delete(RootPath, true);
            _disposed = true;
        }

        private void VerifyDisposableIdentity()
        {
            var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            var actualParent = Directory.GetParent(RootPath)?.FullName.TrimEnd(Path.DirectorySeparatorChar);

            if (!string.Equals(expectedParent, actualParent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(RootPath).StartsWith(
                    "home-ledger-portability-export-",
                    StringComparison.Ordinal)
                || !File.Exists(MarkerPath)
                || !string.Equals(File.ReadAllText(MarkerPath), _identity, StringComparison.Ordinal)
                || (File.GetAttributes(RootPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to remove unverified portability export root '{RootPath}'.");
            }
        }
    }
}
