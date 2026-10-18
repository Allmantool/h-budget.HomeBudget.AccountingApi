using HomeBudget.Accounting.Api.Configuration;

using Microsoft.Extensions.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal sealed class ValidatedDataProtectionRuntimeReadinessProbe(
        IOptions<DataProtectionRecoveryOptions> options)
        : IDataProtectionRuntimeReadinessProbe
    {
        public DataProtectionRuntimeReadinessEvidence GetEvidence()
        {
            var configured = options.Value;
            return new DataProtectionRuntimeReadinessEvidence(
                configured.BackupProvider,
                configured.IndependentStore,
                configured.KeyReference,
                configured.NotificationTarget,
                configured.RecoveryHostResourceId);
        }
    }
}
