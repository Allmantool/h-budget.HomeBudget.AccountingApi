namespace HomeBudget.Accounting.Api.Configuration
{
    internal sealed class UnavailableDataProtectionRuntimeReadinessProbe
        : IDataProtectionRuntimeReadinessProbe
    {
        public DataProtectionRuntimeReadinessEvidence GetEvidence()
        {
            return DataProtectionRuntimeReadinessEvidence.Unavailable;
        }
    }
}
