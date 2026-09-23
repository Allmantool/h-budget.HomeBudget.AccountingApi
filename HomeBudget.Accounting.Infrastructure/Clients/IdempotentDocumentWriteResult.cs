using System;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public sealed record IdempotentDocumentWriteResult(
        Guid TargetId,
        IdempotentDocumentWriteState State)
    {
        public bool IsConflict => State == IdempotentDocumentWriteState.Conflict;
    }
}
