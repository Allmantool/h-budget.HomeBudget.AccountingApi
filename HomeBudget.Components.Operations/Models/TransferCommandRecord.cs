using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record TransferCommandRecord
    {
        public Guid TransferId { get; init; }
        public string CommandId { get; init; }
        public string RequestFingerprint { get; init; }
        public Guid SenderAccountId { get; init; }
        public Guid RecipientAccountId { get; init; }
        public Guid SenderOperationId { get; init; }
        public Guid RecipientOperationId { get; init; }
        public string SenderCommandId { get; init; }
        public string RecipientCommandId { get; init; }
        public decimal SenderAmount { get; init; }
        public decimal RecipientAmount { get; init; }
        public string SenderCurrency { get; init; }
        public string RecipientCurrency { get; init; }
        public DateOnly OperationDate { get; init; }
        public string SourceReference { get; init; }
        public DateTime AcceptedUtc { get; init; }
        public byte SenderStatus { get; init; }
        public byte RecipientStatus { get; init; }
        public DateTime? SenderPublishedUtc { get; init; }
        public DateTime? RecipientPublishedUtc { get; init; }
        public DateTime? SenderPersistedUtc { get; init; }
        public DateTime? RecipientPersistedUtc { get; init; }
        public DateTime? SenderProjectedUtc { get; init; }
        public DateTime? RecipientProjectedUtc { get; init; }
        public string SenderError { get; init; }
        public string RecipientError { get; init; }
    }
}
