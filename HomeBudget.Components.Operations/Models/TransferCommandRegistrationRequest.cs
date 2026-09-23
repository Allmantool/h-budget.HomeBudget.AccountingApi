using System;

using HomeBudget.Accounting.Infrastructure.Data.DbEntries;

namespace HomeBudget.Components.Operations.Models
{
    internal sealed record TransferCommandRegistrationRequest
    {
        public Guid TransferId { get; init; }
        public string CommandId { get; init; }
        public string IdempotencyKeyHash { get; init; }
        public string RequestFingerprint { get; init; }
        public string SourceReference { get; init; }
        public Guid SenderAccountId { get; init; }
        public Guid RecipientAccountId { get; init; }
        public decimal SenderAmount { get; init; }
        public decimal RecipientAmount { get; init; }
        public string SenderCurrency { get; init; }
        public string RecipientCurrency { get; init; }
        public DateOnly OperationDate { get; init; }
        public OutboxAccountPaymentsEntity SenderOutbox { get; init; }
        public OutboxAccountPaymentsEntity RecipientOutbox { get; init; }
    }
}
