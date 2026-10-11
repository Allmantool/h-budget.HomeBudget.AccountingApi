using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Reset
{
    public sealed class LedgerResetException : InvalidOperationException
    {
        public LedgerResetException(string code, string message)
            : base(message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            Code = code;
        }

        public string Code { get; }
    }

    public sealed class LedgerResetActivationOutcomeUnknownException : Exception
    {
    }
}
