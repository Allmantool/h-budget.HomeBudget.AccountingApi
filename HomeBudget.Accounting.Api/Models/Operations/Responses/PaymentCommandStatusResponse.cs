using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public record PaymentCommandStatusResponse
    {
        public string CommandId { get; init; }
        public string PaymentOperationId { get; init; }
        public string CommandType { get; init; }
        public string Status { get; init; }
        public DateTime AcceptedAt { get; init; }
        public DateTime? PublishedAt { get; init; }
        public DateTime? PersistedAt { get; init; }
        public DateTime? ProjectedAt { get; init; }
    }
}
