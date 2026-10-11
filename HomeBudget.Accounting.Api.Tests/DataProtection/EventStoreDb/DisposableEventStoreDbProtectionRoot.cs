using System;
using System.IO;
using System.Linq;
using HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.EventStoreDb
{
    internal sealed class DisposableEventStoreDbProtectionRoot : IDisposable
    {
        private const string MarkerName = ".home-ledger-disposable-eventstoredb-provider";
        private readonly string _safeParent;

        public DisposableEventStoreDbProtectionRoot()
        {
            _safeParent = Path.Combine(Path.GetTempPath(), "home-ledger-eventstoredb-provider-tests");
            Root = Path.Combine(_safeParent, Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(Root, "source", "db");
            IndexPath = Path.Combine(Root, "source", "index");
            ArtifactRoot = Path.Combine(Root, "artifacts");
            TargetDatabasePath = Path.Combine(Root, "target", "db");
            TargetIndexPath = Path.Combine(Root, "target", "index");

            Directory.CreateDirectory(DatabasePath);
            Directory.CreateDirectory(IndexPath);
            Directory.CreateDirectory(ArtifactRoot);
            Directory.CreateDirectory(TargetDatabasePath);
            Directory.CreateDirectory(TargetIndexPath);
            File.WriteAllText(Path.Combine(Root, MarkerName), "process-owned test data");
            WriteValidSourceFiles();

            SourceIdentity = new EventStoreDbNodePathIdentity("source-node-a", DatabasePath, IndexPath);
            TargetIdentity = new EventStoreDbNodePathIdentity(
                "restore-target-a",
                TargetDatabasePath,
                TargetIndexPath);
            Probe = new MutableEventStoreDbProtectionProbe(CreateSourceProbe(), CreateTargetProbe());
            Policy = new EventStoreDbProtectionPolicy(
                "24.10.16",
                SourceIdentity,
                new[] { SourceIdentity },
                new[] { ArtifactRoot },
                new[] { TargetIdentity });
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public string IndexPath { get; }

        public string ArtifactRoot { get; }

        public string TargetDatabasePath { get; }

        public string TargetIndexPath { get; }

        public EventStoreDbNodePathIdentity SourceIdentity { get; }

        public EventStoreDbNodePathIdentity TargetIdentity { get; }

        public MutableEventStoreDbProtectionProbe Probe { get; }

        public EventStoreDbProtectionPolicy Policy { get; }

        public EventStoreDbProtectionProvider CreateProvider() => new(Policy, Probe, TimeProvider.System);

        public EventStoreDbBackupSet MaterializeBackup(EventStoreDbCapturePlan plan, bool verified = true)
        {
            var captureRoot = Path.Combine(ArtifactRoot, plan.PlanId.ToString("N"));
            Directory.CreateDirectory(captureRoot);
            foreach (var file in plan.Inventory)
            {
                var source = GetSourcePath(file.RelativePath);
                var destination = Path.Combine(captureRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }

            return new EventStoreDbBackupSet(
                plan.PlanId,
                plan.Kind,
                plan.SourceNodeId,
                plan.ServerVersion,
                plan.WriterPosition,
                plan.ChaserPosition,
                captureRoot,
                plan.ParentCaptureId,
                verified,
                plan.Inventory);
        }

        public void Dispose()
        {
            var root = Path.GetFullPath(Root);
            var parent = Path.GetFullPath(_safeParent) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(root, MarkerName)))
            {
                throw new InvalidOperationException("Refusing to remove an unverified test directory.");
            }

            Directory.Delete(root, true);
        }

        private EventStoreDbSourceProbe CreateSourceProbe()
        {
            var expectedIndexFiles = Directory.EnumerateFiles(IndexPath, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(IndexPath, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return new EventStoreDbSourceProbe(
                SourceIdentity,
                "24.10.16",
                true,
                true,
                false,
                4096,
                3072,
                expectedIndexFiles,
                DateTimeOffset.UtcNow);
        }

        private EventStoreDbRestoreTargetProbe CreateTargetProbe() => new(
            TargetIdentity,
            false,
            true,
            true,
            DateTimeOffset.UtcNow);

        private string GetSourcePath(string relativePath)
        {
            var separator = relativePath.IndexOf('/', StringComparison.Ordinal);
            var area = relativePath[..separator];
            var child = relativePath[(separator + 1)..].Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(area == "db" ? DatabasePath : IndexPath, child);
        }

        private void WriteValidSourceFiles()
        {
            foreach (var checkpoint in new[] { "chaser.chk", "epoch.chk", "proposal.chk", "truncate.chk", "writer.chk" })
            {
                File.WriteAllText(Path.Combine(DatabasePath, checkpoint), checkpoint + "-contents");
            }

            File.WriteAllText(Path.Combine(DatabasePath, "chunk-000000.000000"), "chunk-zero");
            File.WriteAllText(Path.Combine(DatabasePath, "chunk-000001.000000"), "chunk-one");
            File.WriteAllText(Path.Combine(IndexPath, "indexmap"), "index-map-v1");
            File.WriteAllText(Path.Combine(IndexPath, "index.checkpoint.chk"), "index-checkpoint");
            File.WriteAllText(
                Path.Combine(IndexPath, "5a1a8395-94ee-40c1-bf93-aa757b5887f8"),
                "index-table");
            File.WriteAllText(
                Path.Combine(IndexPath, "5a1a8395-94ee-40c1-bf93-aa757b5887f8.bloomfilter"),
                "bloom-filter");
            Directory.CreateDirectory(Path.Combine(IndexPath, "stream-existence"));
            File.WriteAllText(
                Path.Combine(IndexPath, "stream-existence", "streamExistenceFilter.chk"),
                "stream-checkpoint");
            File.WriteAllText(
                Path.Combine(IndexPath, "stream-existence", "streamExistenceFilter.dat"),
                "stream-data");
        }
    }
}
