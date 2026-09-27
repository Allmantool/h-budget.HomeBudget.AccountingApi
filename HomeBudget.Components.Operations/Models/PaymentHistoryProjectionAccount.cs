using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record PaymentHistoryProjectionAccount
    {
        public Guid PaymentAccountId { get; init; }
        public long NextBalanceFence { get; init; }
    }
}
