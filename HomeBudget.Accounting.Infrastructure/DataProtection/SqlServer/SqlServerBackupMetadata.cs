using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public sealed record SqlServerBackupMetadata
    {
        public long BackupSetId { get; init; }

        public Guid BackupSetUuid { get; init; }

        public string DatabaseName { get; init; }

        public SqlServerBackupKind Kind { get; init; }

        public decimal FirstLsn { get; init; }

        public decimal LastLsn { get; init; }

        public decimal CheckpointLsn { get; init; }

        public decimal DatabaseBackupLsn { get; init; }

        public decimal? DifferentialBaseLsn { get; init; }

        public Guid RecoveryForkId { get; init; }

        public DateTimeOffset BackupStartUtc { get; init; }

        public DateTimeOffset BackupFinishUtc { get; init; }

        public DateTimeOffset? LastValidRestoreTimeUtc { get; init; }

        public bool HasBackupChecksums { get; init; }

        public bool IsDamaged { get; init; }

        public bool HasIncompleteMetadata { get; init; }

        public bool BeginsLogChain { get; init; }

        public bool IsVerified { get; init; }

        public long BackupSizeBytes { get; init; }

        public long CompressedBackupSizeBytes { get; init; }

        public string RecoveryModel { get; init; }

        public string PhysicalDeviceName { get; init; }
    }
}
