using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed class RecoveryPlanningException : Exception
    {
        public RecoveryPlanningException()
            : this("recovery-planning-failed", "Recovery planning failed.")
        {
        }

        public RecoveryPlanningException(string message)
            : this("recovery-planning-failed", message)
        {
        }

        public RecoveryPlanningException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "recovery-planning-failed";
        }

        public RecoveryPlanningException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
