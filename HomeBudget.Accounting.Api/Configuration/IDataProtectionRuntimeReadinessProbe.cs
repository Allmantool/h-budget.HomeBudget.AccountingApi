namespace HomeBudget.Accounting.Api.Configuration
{
    public interface IDataProtectionRuntimeReadinessProbe
    {
        DataProtectionRuntimeReadinessEvidence GetEvidence();
    }
}
