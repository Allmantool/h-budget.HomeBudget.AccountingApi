using System;
using System.IO;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed partial class EventStoreDbProtectionProvider
    {
        private void ValidateSourceProbe(EventStoreDbSourceProbe probe)
        {
            if (probe is null)
            {
                throw Failure("source-probe-unavailable", "The source-node probe is unavailable.");
            }

            if (!string.Equals(probe.ServerVersion, _policy.ExpectedVersion, StringComparison.Ordinal))
            {
                throw Failure("unsupported-version", "The EventStoreDB server version is unsupported.");
            }

            if (!IdentityEquals(probe.Identity, _policy.Source) ||
                !_policy.AllowedSources.Any(source => IdentityEquals(probe.Identity, source)))
            {
                throw Failure("source-identity-mismatch", "The EventStoreDB source identity is not allowlisted.");
            }

            if (!probe.IsClusterConnected || !probe.IsUpToDate)
            {
                throw Failure("source-not-current", "The EventStoreDB source node is not current and connected.");
            }

            if (probe.IsScavengeRunning)
            {
                throw Failure("scavenge-active", "File-copy capture is blocked while scavenge is active.");
            }

            if (probe.ChaserPosition < 0 || probe.WriterPosition < 0 ||
                probe.ChaserPosition > probe.WriterPosition)
            {
                throw Failure("checkpoint-position-invalid", "The writer and chaser positions are inconsistent.");
            }
        }

        private void ValidatePreviousBackup(
            EventStoreDbCaptureKind kind,
            EventStoreDbBackupSet previousBackup,
            string sourceNodeId)
        {
            if (kind == EventStoreDbCaptureKind.FullFileCopy)
            {
                if (previousBackup is not null)
                {
                    throw Failure("full-parent-not-allowed", "A full capture cannot depend on another capture.");
                }

                return;
            }

            if (kind != EventStoreDbCaptureKind.DifferentialFileCopy)
            {
                throw Failure("capture-kind-unsupported", "The capture kind is unsupported.");
            }

            if (previousBackup is null || !previousBackup.IsIntegrityVerified)
            {
                throw Failure("differential-baseline-invalid", "A verified prior backup is required.");
            }

            ValidateBackupForRestore(previousBackup);
            ValidateBackupIdentity(previousBackup, sourceNodeId);
        }

        private void ValidateStableSource(EventStoreDbSourceProbe initial, EventStoreDbSourceProbe final)
        {
            ValidateSourceProbe(final);
            if (!IdentityEquals(initial.Identity, final.Identity) ||
                !string.Equals(initial.ServerVersion, final.ServerVersion, StringComparison.Ordinal) ||
                initial.WriterPosition != final.WriterPosition ||
                initial.ChaserPosition != final.ChaserPosition ||
                !initial.ExpectedIndexFiles.OrderBy(path => path, StringComparer.Ordinal)
                    .SequenceEqual(final.ExpectedIndexFiles.OrderBy(path => path, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw Failure("source-changed-during-plan", "The EventStoreDB source changed during planning.");
            }
        }

        private void ValidateBackupIdentity(EventStoreDbBackupSet backup, string expectedNodeId)
        {
            if (!string.Equals(backup.SourceNodeId, expectedNodeId, StringComparison.Ordinal) ||
                backup.Inventory is null ||
                backup.Inventory.Any(file => !string.Equals(file.SourceNodeId, expectedNodeId, StringComparison.Ordinal)))
            {
                throw Failure("backup-node-mismatch", "Backup files from different source nodes cannot be combined.");
            }

            if (!string.Equals(backup.ServerVersion, _policy.ExpectedVersion, StringComparison.Ordinal))
            {
                throw Failure("unsupported-version", "The EventStoreDB backup version is unsupported.");
            }

            if (backup.Inventory.GroupBy(file => file.RelativePath, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                throw Failure("backup-inventory-invalid", "The backup inventory contains duplicate paths.");
            }
        }

        private static bool IdentityEquals(
            EventStoreDbNodePathIdentity actual,
            EventStoreDbNodePathIdentity expected)
        {
            if (actual is null || expected is null)
            {
                return false;
            }

            try
            {
                return string.Equals(actual.NodeId, expected.NodeId, StringComparison.Ordinal) &&
                    EventStoreDbProtectionPolicy.PathComparer.Equals(
                        EventStoreDbProtectionPolicy.NormalizePath(actual.DatabasePath, nameof(actual)),
                        expected.DatabasePath) &&
                    EventStoreDbProtectionPolicy.PathComparer.Equals(
                        EventStoreDbProtectionPolicy.NormalizePath(actual.IndexPath, nameof(actual)),
                        expected.IndexPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        private static EventStoreDbProtectionException Failure(string code, string safeMessage) => new(code, safeMessage);
    }
}
