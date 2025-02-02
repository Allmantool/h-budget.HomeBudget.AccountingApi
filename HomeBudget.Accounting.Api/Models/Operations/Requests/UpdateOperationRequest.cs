using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record UpdateOperationRequest
    {
        public decimal Amount { get; init; }
        public string Comment { get; init; }
        public string ContractorId { get; init; }
        public string CategoryId { get; init; }
        public DateOnly OperationDate { get; init; }
        public string ScopeOperationId { get; init; }
    }
}
