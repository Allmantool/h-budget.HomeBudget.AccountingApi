using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    public sealed class MaintenanceAdmissionClosedException : InvalidOperationException
    {
        public MaintenanceAdmissionClosedException()
            : base("The ledger is temporarily closed for maintenance.")
        {
        }

        public MaintenanceAdmissionClosedException(string message)
            : base(message)
        {
        }

        public MaintenanceAdmissionClosedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
