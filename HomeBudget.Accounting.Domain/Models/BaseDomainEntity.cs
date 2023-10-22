using System;

namespace HomeBudget.Accounting.Domain.Models
{
    public abstract class BaseDomainEntity
    {
        public Guid Id { get; set; }

        public long OperationUnixTime { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
