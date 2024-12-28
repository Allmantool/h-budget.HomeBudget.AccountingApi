using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record RemoveOperationRequest
    {
        public string PaymentAccountId { get; init; }
        public string OperationId { get; init; }
        public DateOnly OperationDate { get; init; }
    }
}
