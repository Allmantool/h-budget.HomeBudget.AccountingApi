namespace HomeBudget.Accounting.Api.Models.Operation
{
    public class CreateOperationRequest
    {
        public decimal Amount { get; set; }
        public string Comment { get; set; }
        public string ContractorId { get; set; }
        public string CategoryId { get; set; }
    }
}
