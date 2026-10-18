using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed class PortabilityImportReconciliationOptions
    {
        public int MaximumRecords { get; set; } = 100_000;

        public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromMinutes(2);

        internal void Validate()
        {
            if (MaximumRecords < 1 || MaximumRecords > 1_000_000 || ReadTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumRecords));
            }
        }
    }
}
