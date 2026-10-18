using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public sealed record AccountDeliveryDisposition
    {
        required public string MessageId { get; init; }

        required public string LedgerId { get; init; }

        required public string LedgerGenerationId { get; init; }

        required public string PayloadSha256 { get; init; }

        required public string Reason { get; init; }

        required public string Topic { get; init; }

        public int Partition { get; init; }

        public long Offset { get; init; }

        public DateTime DisposedUtc { get; init; }
    }
}
