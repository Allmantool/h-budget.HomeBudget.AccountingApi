using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed class DataProtectionOperationFenceContext
    {
        public Guid OperationId { get; init; }
        public Guid LeaseId { get; init; }
        public string ScopeKey { get; init; }
        public string OwnerId { get; init; }
        public long FenceToken { get; init; }
    }
}
