using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public sealed record TransferCommandStatusResponse
    {
        public Guid TransferId { get; init; }
        public string CommandId { get; init; }
        public string Status { get; init; }
        public Guid SenderAccountId { get; init; }
        public Guid RecipientAccountId { get; init; }
        public Guid SenderOperationId { get; init; }
        public Guid RecipientOperationId { get; init; }
        public decimal SenderAmount { get; init; }
        public decimal RecipientAmount { get; init; }
        public string SenderCurrency { get; init; }
        public string RecipientCurrency { get; init; }
        public DateOnly OperationDate { get; init; }
        public string SourceReference { get; init; }
        public DateTime AcceptedAt { get; init; }
        public DateTime? PublishedAt { get; init; }
        public DateTime? PersistedAt { get; init; }
        public DateTime? ProjectedAt { get; init; }
        public string Failure { get; init; }
    }
}
