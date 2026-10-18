using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed class RecoveryExecutionException : Exception
    {
        public RecoveryExecutionException()
            : this("restore-execution-failed", "Isolated restore execution failed.")
        {
        }

        public RecoveryExecutionException(string message)
            : this("restore-execution-failed", message)
        {
        }

        public RecoveryExecutionException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "restore-execution-failed";
        }

        public RecoveryExecutionException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
