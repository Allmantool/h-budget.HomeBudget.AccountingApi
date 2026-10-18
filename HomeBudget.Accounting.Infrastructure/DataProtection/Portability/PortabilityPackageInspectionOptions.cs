using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability
{
    internal sealed class PortabilityPackageInspectionOptions
    {
        public int MaxEntryCount { get; init; } = 128;

        public long MaxArchiveCompressedBytes { get; init; } = 512L * 1024 * 1024;

        public long MaxEntryCompressedBytes { get; init; } = 128L * 1024 * 1024;

        public long MaxTotalUncompressedBytes { get; init; } = 1024L * 1024 * 1024;

        public long MaxEntryUncompressedBytes { get; init; } = 256L * 1024 * 1024;

        public double MaxExpansionRatio { get; init; } = 100;

        public int MaxJsonLineBytes { get; init; } = 1024 * 1024;

        public int MaxJsonlRecordCount { get; init; } = 1_000_000;

        public int MaxJsonDepth { get; init; } = 64;

        public int BufferSizeBytes { get; init; } = 64 * 1024;

        public void Validate()
        {
            if (MaxEntryCount <= 0
                || MaxArchiveCompressedBytes <= 0
                || MaxEntryCompressedBytes <= 0
                || MaxTotalUncompressedBytes <= 0
                || MaxEntryUncompressedBytes <= 0
                || MaxExpansionRatio <= 0
                || MaxJsonLineBytes <= 0
                || MaxJsonlRecordCount <= 0
                || MaxJsonDepth <= 0
                || BufferSizeBytes is < 4096 or > 1024 * 1024)
            {
                throw new InvalidOperationException("Portability package inspection limits must be positive and bounded.");
            }
        }
    }
}
