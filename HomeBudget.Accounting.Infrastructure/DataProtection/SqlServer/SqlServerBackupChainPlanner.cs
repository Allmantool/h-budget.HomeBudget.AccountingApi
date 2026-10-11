using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    internal static class SqlServerBackupChainPlanner
    {
        public static void Validate(IReadOnlyCollection<SqlServerBackupMetadata> backups)
        {
            var ordered = RequireValidBackups(backups);
            var baseline = SelectBaseline(ordered, DateTimeOffset.MaxValue);
            BuildLogChain(baseline.Anchor, baseline.Full.RecoveryForkId, ordered, null);
        }

        public static SqlServerPointInTimeRecoveryPlan Plan(
            DateTimeOffset requestedCutoffUtc,
            IReadOnlyCollection<SqlServerBackupMetadata> backups)
        {
            var requested = requestedCutoffUtc.ToUniversalTime();
            var ordered = RequireValidBackups(backups);
            var baseline = SelectBaseline(ordered, requested);
            var logs = BuildLogChain(
                baseline.Anchor,
                baseline.Full.RecoveryForkId,
                ordered,
                requested);
            var selected = baseline.Differential is null
                ? new[] { baseline.Full }.Concat(logs).ToArray()
                : new[] { baseline.Full, baseline.Differential }.Concat(logs).ToArray();

            if (logs.Count == 0 || logs[^1].LastValidRestoreTimeUtc <= requested)
            {
                throw Invalid("The SQL backup chain does not cover the requested STOPAT timestamp.");
            }

            return new SqlServerPointInTimeRecoveryPlan(
                baseline.Full.DatabaseName,
                requested,
                requested,
                requested,
                "sql-server-2022-stopat",
                selected,
                selected[0].FirstLsn,
                selected[^1].LastLsn);
        }

        private static IReadOnlyList<SqlServerBackupMetadata> RequireValidBackups(
            IReadOnlyCollection<SqlServerBackupMetadata> backups)
        {
            if (backups is null || backups.Count == 0)
            {
                throw Invalid("A SQL backup chain requires a full baseline.");
            }

            var ordered = backups.OrderBy(item => item.BackupStartUtc).ThenBy(item => item.BackupSetId).ToArray();
            var database = ordered[0].DatabaseName;
            foreach (var backup in ordered)
            {
                if (!string.Equals(backup.DatabaseName, database, StringComparison.Ordinal))
                {
                    throw Invalid("A SQL backup chain cannot mix databases.");
                }

                if (!backup.HasBackupChecksums || !backup.IsVerified || backup.IsDamaged || backup.HasIncompleteMetadata)
                {
                    throw Invalid("A SQL backup chain contains an unverified, damaged, or incomplete backup set.");
                }

                if (!string.Equals(backup.RecoveryModel, "FULL", StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid("A SQL backup chain requires the FULL recovery model.");
                }
            }

            return ordered;
        }

        private static Baseline SelectBaseline(
            IReadOnlyList<SqlServerBackupMetadata> ordered,
            DateTimeOffset requested)
        {
            var full = ordered.LastOrDefault(item =>
                item.Kind == SqlServerBackupKind.Full && item.BackupFinishUtc <= requested);
            if (full is null)
            {
                throw Invalid("No complete full SQL backup exists at or before the requested cutoff.");
            }

            var differential = ordered.LastOrDefault(item =>
                item.Kind == SqlServerBackupKind.Differential &&
                item.BackupFinishUtc <= requested &&
                item.RecoveryForkId == full.RecoveryForkId &&
                item.DifferentialBaseLsn == full.CheckpointLsn);
            var anchor = differential?.LastLsn ?? full.LastLsn;
            return new Baseline(full, differential, anchor);
        }

        private static IReadOnlyList<SqlServerBackupMetadata> BuildLogChain(
            decimal anchor,
            Guid recoveryForkId,
            IReadOnlyList<SqlServerBackupMetadata> ordered,
            DateTimeOffset? requested)
        {
            var candidates = ordered.Where(item =>
                    item.Kind == SqlServerBackupKind.Log && item.RecoveryForkId == recoveryForkId)
                .OrderBy(item => item.FirstLsn)
                .ThenBy(item => item.BackupSetId)
                .ToArray();
            var chain = new List<SqlServerBackupMetadata>();
            var cursor = anchor;

            foreach (var log in candidates)
            {
                if (log.LastLsn <= cursor)
                {
                    continue;
                }

                if (log.FirstLsn > cursor)
                {
                    throw Invalid($"The SQL transaction-log chain has an LSN gap after {cursor}.");
                }

                chain.Add(log);
                cursor = log.LastLsn;
                if (requested.HasValue && log.LastValidRestoreTimeUtc > requested.Value)
                {
                    break;
                }
            }

            return chain;
        }

        private static SqlServerBackupChainValidationException Invalid(string message)
        {
            return new SqlServerBackupChainValidationException(message);
        }

        private sealed record Baseline(
            SqlServerBackupMetadata Full,
            SqlServerBackupMetadata Differential,
            decimal Anchor);
    }
}
