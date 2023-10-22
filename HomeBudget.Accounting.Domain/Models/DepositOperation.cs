using System;

namespace HomeBudget.Accounting.Domain.Models
{
    public class DepositOperation
    {
        public Guid Id { get; set; }
        public long OperationUnixTime { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
