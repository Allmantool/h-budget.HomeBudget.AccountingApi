using System;
using System.IO;
using System.Linq;
using System.Text;

using FluentAssertions;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    internal sealed class DisposablePortabilityTestRoot : IDisposable
    {
        private const string MarkerFileName = ".home-ledger-disposable-test-root";
        private readonly string _identity = Guid.NewGuid().ToString("N");
        private bool _disposed;

        public DisposablePortabilityTestRoot()
        {
            RootPath = Path.GetFullPath(
                Directory.CreateTempSubdirectory("home-ledger-portability-security-").FullName);
            MarkerPath = Path.Combine(RootPath, MarkerFileName);
            File.WriteAllText(MarkerPath, _identity, new UTF8Encoding(false));
            ExtractionPath = Directory.CreateDirectory(Path.Combine(RootPath, "extracted")).FullName;
        }

        public string RootPath { get; }

        public string ExtractionPath { get; }

        private string MarkerPath { get; }

        public void AssertNoArchiveOutputRemains()
        {
            Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(RootPath, path).Replace('\\', '/'))
                .Should().Equal(MarkerFileName);

            Directory.EnumerateDirectories(RootPath, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(RootPath, path).Replace('\\', '/'))
                .Should().Equal("extracted");
        }

        public void ResetExtraction()
        {
            VerifyDisposableIdentity();
            if (!string.Equals(
                    Directory.GetParent(ExtractionPath)?.FullName,
                    RootPath,
                    StringComparison.OrdinalIgnoreCase)
                || (File.GetAttributes(ExtractionPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Refusing to reset an unverified extraction directory.");
            }

            Directory.Delete(ExtractionPath, true);
            Directory.CreateDirectory(ExtractionPath);
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
                || !Path.GetFileName(RootPath).StartsWith("home-ledger-portability-security-", StringComparison.Ordinal)
                || !File.Exists(MarkerPath)
                || !string.Equals(File.ReadAllText(MarkerPath), _identity, StringComparison.Ordinal)
                || (File.GetAttributes(RootPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to remove unverified portability test root '{RootPath}'.");
            }
        }
    }
}
