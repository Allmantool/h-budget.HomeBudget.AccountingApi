using System;
using System.IO;
using HomeBudget.Accounting.Infrastructure.DataProtection.Artifacts;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Artifacts
{
    internal sealed class DisposableArtifactTestRoot : IDisposable
    {
        private readonly string _ownershipToken = Guid.NewGuid().ToString("N");

        public DisposableArtifactTestRoot(string rootId)
        {
            ParentPath = Path.Combine(Path.GetTempPath(), "home-ledger-artifact-tests", Guid.NewGuid().ToString("N"));
            RootPath = Path.Combine(ParentPath, rootId);
            Directory.CreateDirectory(RootPath);
            File.WriteAllText(
                Path.Combine(RootPath, DisposableFileSystemArtifactStore.MarkerFileName),
                $"{rootId}\n{_ownershipToken}");
            Identity = new DisposableArtifactRootIdentity(
                RootPath,
                ParentPath,
                rootId,
                _ownershipToken);
        }

        public string ParentPath { get; }

        public string RootPath { get; }

        public DisposableArtifactRootIdentity Identity { get; }

        public void Dispose()
        {
            var resolvedParent = Path.GetFullPath(ParentPath);
            var resolvedRoot = Path.GetFullPath(RootPath);
            var markerPath = Path.Combine(resolvedRoot, DisposableFileSystemArtifactStore.MarkerFileName);
            if (!resolvedRoot.StartsWith(resolvedParent + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                !File.Exists(markerPath) ||
                !string.Equals(File.ReadAllText(markerPath), $"{Identity.RootId}\n{_ownershipToken}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Disposable artifact root identity changed; cleanup refused.");
            }

            foreach (var file in Directory.EnumerateFiles(resolvedRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(resolvedParent, true);
        }
    }
}
