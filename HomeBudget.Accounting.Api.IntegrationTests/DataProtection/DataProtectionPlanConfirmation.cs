using System;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal sealed class DataProtectionPlanConfirmation
    {
        public DataProtectionPlanConfirmation(
            Guid planId,
            string planFingerprint,
            string confirmationToken)
        {
            PlanId = planId;
            PlanFingerprint = planFingerprint;
            ConfirmationToken = confirmationToken;
        }

        public Guid PlanId { get; }
        public string PlanFingerprint { get; }
        public string ConfirmationToken { get; }

        public DataProtectionPlanConfirmation WithPlanFingerprint(string planFingerprint)
        {
            return new DataProtectionPlanConfirmation(PlanId, planFingerprint, ConfirmationToken);
        }
    }
}
