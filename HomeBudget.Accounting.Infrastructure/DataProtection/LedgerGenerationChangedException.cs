using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed class LedgerGenerationChangedException : Exception
    {
        public LedgerGenerationChangedException()
        {
        }

        public LedgerGenerationChangedException(string message)
            : base(message)
        {
        }

        public LedgerGenerationChangedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public LedgerGenerationChangedException(Exception innerException)
            : base("The active ledger generation changed before durable command acceptance.", innerException)
        {
        }
    }
}
