using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed partial class EventStoreDbProtectionProvider
    {
        private async Task<EventStoreDbRestorePlan> PlanRestoreCoreAsync(
            Guid planId,
            EventStoreDbBackupSet backup,
            string targetId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (planId == Guid.Empty || string.IsNullOrWhiteSpace(targetId))
            {
                throw Failure("restore-request-invalid", "The restore planning request is invalid.");
            }

            ValidateBackupForRestore(backup);
            var target = await _probe.ProbeRestoreTargetAsync(targetId, cancellationToken).ConfigureAwait(false);
            ValidateRestoreTarget(target, targetId);
            await VerifyArtifactAsync(backup, cancellationToken).ConfigureAwait(false);

            var steps = backup.Inventory
                .OrderBy(file => GetCopyOrder(file.Role))
                .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
                .Select(file => new EventStoreDbCopyStep(
                    GetCopyOrder(file.Role),
                    EventStoreDbCopyAction.Copy,
                    GetArtifactFilePath(backup.ArtifactRoot, file.RelativePath),
                    file.RelativePath,
                    file.Sha256,
                    file.Dependencies))
                .ToArray();
            return new EventStoreDbRestorePlan(
                planId,
                backup.CaptureId,
                backup.SourceNodeId,
                targetId,
                backup.WriterPosition,
                backup.ChaserPosition,
                _timeProvider.GetUtcNow(),
                steps,
                "db/chaser.chk",
                "db/truncate.chk");
        }

        private void ValidateBackupForRestore(EventStoreDbBackupSet backup)
        {
            if (backup is null || !backup.IsIntegrityVerified || backup.CaptureId == Guid.Empty)
            {
                throw Failure("backup-not-verified", "A verified EventStoreDB backup is required.");
            }

            ValidateBackupIdentity(backup, _policy.Source.NodeId);
            if (backup.ChaserPosition < 0 || backup.WriterPosition < 0 ||
                backup.ChaserPosition > backup.WriterPosition)
            {
                throw Failure("checkpoint-position-invalid", "The writer and chaser positions are inconsistent.");
            }

            var inventoryPaths = backup.Inventory.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
            if (RequiredDatabaseCheckpoints.Any(file => !inventoryPaths.Contains("db/" + file)) ||
                !backup.Inventory.Any(file => file.Role == EventStoreDbFileRole.Chunk) ||
                !inventoryPaths.Contains("index/indexmap") ||
                !backup.Inventory.Any(file => file.Role == EventStoreDbFileRole.IndexTable))
            {
                throw Failure("backup-inventory-incomplete", "The EventStoreDB backup inventory is incomplete.");
            }

            foreach (var file in backup.Inventory)
            {
                if (file.SizeBytes < 0 || file.Sha256 is null || file.Sha256.Length != 64 ||
                    file.Dependencies.Any(dependency => !inventoryPaths.Contains(dependency)))
                {
                    throw Failure("backup-inventory-invalid", "The EventStoreDB backup inventory is invalid.");
                }
            }

            if (!_policy.AllowedArtifactRoots.Any(root => IsSameOrDescendant(backup.ArtifactRoot, root)))
            {
                throw Failure("artifact-root-not-allowlisted", "The backup artifact root is not allowlisted.");
            }
        }

        private void ValidateRestoreTarget(EventStoreDbRestoreTargetProbe target, string requestedTargetId)
        {
            if (target is null ||
                !string.Equals(target.Identity?.NodeId, requestedTargetId, StringComparison.Ordinal) ||
                !_policy.AllowedRestoreTargets.Any(allowed => IdentityEquals(target.Identity, allowed)))
            {
                throw Failure("restore-target-mismatch", "The restore target identity is not allowlisted.");
            }

            if (target.IsProcessRunning)
            {
                throw Failure("restore-target-running", "The EventStoreDB restore target must be stopped.");
            }

            if (!target.IsDedicated)
            {
                throw Failure("restore-target-shared", "The EventStoreDB restore target is not dedicated.");
            }

            if (!target.IsEmpty)
            {
                throw Failure("restore-target-not-empty", "The EventStoreDB restore target is not empty.");
            }

            EnsureEmptyTargetDirectory(target.Identity.DatabasePath);
            EnsureEmptyTargetDirectory(target.Identity.IndexPath);
        }

        private static async Task VerifyArtifactAsync(
            EventStoreDbBackupSet backup,
            CancellationToken cancellationToken)
        {
            EnsureSafeDirectory(backup.ArtifactRoot);
            var expectedPaths = backup.Inventory.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
            var actualPaths = Directory.EnumerateFiles(backup.ArtifactRoot, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(backup.ArtifactRoot, path)))
                .ToHashSet(StringComparer.Ordinal);
            if (!actualPaths.SetEquals(expectedPaths))
            {
                throw Failure("backup-inventory-incomplete", "The EventStoreDB backup artifact is incomplete.");
            }

            foreach (var file in backup.Inventory)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = GetArtifactFilePath(backup.ArtifactRoot, file.RelativePath);
                EnsureSafeFile(path);
                var information = new FileInfo(path);
                var checksum = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
                if (information.Length != file.SizeBytes ||
                    !string.Equals(checksum, file.Sha256, StringComparison.Ordinal))
                {
                    throw Failure("backup-checksum-mismatch", "An EventStoreDB backup checksum does not match.");
                }
            }
        }

        private static string GetArtifactFilePath(string artifactRoot, string relativePath)
        {
            var normalized = NormalizeRelativePath(relativePath);
            var path = Path.GetFullPath(Path.Combine(
                artifactRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsSameOrDescendant(path, artifactRoot))
            {
                throw Failure("inventory-path-invalid", "The EventStoreDB inventory contains an invalid path.");
            }

            return path;
        }

        private static bool IsSameOrDescendant(string candidate, string root)
        {
            try
            {
                var fullCandidate = EventStoreDbProtectionPolicy.NormalizePath(candidate, nameof(candidate));
                var fullRoot = EventStoreDbProtectionPolicy.NormalizePath(root, nameof(root));
                return EventStoreDbProtectionPolicy.PathComparer.Equals(fullCandidate, fullRoot) ||
                    fullCandidate.StartsWith(
                        fullRoot + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        private static void EnsureEmptyTargetDirectory(string path)
        {
            EnsureSafeDirectory(path);
            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                throw Failure("restore-target-not-empty", "The EventStoreDB restore target is not empty.");
            }
        }
    }
}
