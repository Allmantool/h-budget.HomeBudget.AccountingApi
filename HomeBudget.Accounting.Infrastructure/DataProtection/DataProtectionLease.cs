using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class DataProtectionLease
    {
        public Guid LeaseId { get; init; }
        public Guid OperationId { get; init; }
        public string ScopeKey { get; init; }
        public string OwnerId { get; init; }
        public long FenceToken { get; init; }
        public DateTime ExpiresUtc { get; init; }

        public DataProtectionOperationFenceContext CreateFenceContext()
        {
            return new DataProtectionOperationFenceContext
            {
                OperationId = OperationId,
                LeaseId = LeaseId,
                ScopeKey = ScopeKey,
                OwnerId = OwnerId,
                FenceToken = FenceToken
            };
        }
    }
}
