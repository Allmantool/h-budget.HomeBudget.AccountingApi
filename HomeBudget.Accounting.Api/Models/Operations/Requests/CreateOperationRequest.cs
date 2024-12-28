using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record CreateOperationRequest
    {
        public decimal Amount { get; init; }
        public string Comment { get; init; }
        public string ContractorId { get; init; }
        public string CategoryId { get; init; }
        public DateOnly OperationDate { get; init; }
    }
}
