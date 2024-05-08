using System;

namespace HomeBudget.Accounting.Api.Models.Operations.Requests
{
    public record UpdateOperationRequest
    {
        public decimal Amount { get; set; }
        public string Comment { get; set; }
        public string ContractorId { get; set; }
        public string CategoryId { get; set; }
        public DateOnly OperationDate { get; set; }
    }
}
