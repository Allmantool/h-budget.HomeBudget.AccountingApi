using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed class PortabilityImportExecutionOptions
    {
        public int BatchSize { get; set; } = 100;

        public TimeSpan SettlementTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public TimeSpan SettlementPollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        internal void Validate()
        {
            if (BatchSize < 1
                || BatchSize > 1000
                || SettlementTimeout <= TimeSpan.Zero
                || SettlementPollInterval <= TimeSpan.Zero
                || SettlementPollInterval > SettlementTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(BatchSize));
            }
        }
    }
}
