using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed class AccountDeliveryDispositionConflictException : Exception
    {
        public AccountDeliveryDispositionConflictException()
        {
        }

        public AccountDeliveryDispositionConflictException(string message)
            : base(message)
        {
        }

        public AccountDeliveryDispositionConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
