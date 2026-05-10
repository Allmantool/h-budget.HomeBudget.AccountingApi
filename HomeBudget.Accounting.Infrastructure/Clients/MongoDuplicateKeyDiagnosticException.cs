using System;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public sealed class MongoDuplicateKeyDiagnosticException : InvalidOperationException
    {
        public MongoDuplicateKeyDiagnosticException(string message)
            : base(message)
        {
        }
    }
}
