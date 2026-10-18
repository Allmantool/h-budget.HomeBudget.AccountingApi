namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed class PortabilityImportPreflightOptions
    {
        public long MaxRecords { get; set; } = 1_000_000;

        public int CapacityHeadroomMultiplier { get; set; } = 2;

        internal void Validate()
        {
            if (MaxRecords < 1 || CapacityHeadroomMultiplier < 1)
            {
                throw new System.ArgumentOutOfRangeException(nameof(MaxRecords));
            }
        }
    }
}
