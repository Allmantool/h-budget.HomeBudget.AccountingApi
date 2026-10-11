using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    public sealed class PortabilityExportOptions
    {
        public int BufferSizeBytes { get; set; } = 64 * 1024;

        public int MaxBufferedRecords { get; set; } = 1_000;

        public long MaxExportRecords { get; set; } = 1_000_000;

        public TimeSpan ProjectionCatchupTimeout { get; set; } = TimeSpan.FromMinutes(2);

        internal void Validate()
        {
            if (BufferSizeBytes < 4096 || MaxBufferedRecords < 1 || MaxExportRecords < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(BufferSizeBytes), "Export limits must be positive and bounded.");
            }

            if (ProjectionCatchupTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(ProjectionCatchupTimeout));
            }
        }
    }
}
