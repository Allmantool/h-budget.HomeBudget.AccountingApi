namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public record CreateOperationResponse
    {
        public string PaymentAccountId { get; set; }
        public string PaymentOperationId { get; set; }
        public string CommandId { get; set; }
        public string Status { get; set; }
        public bool IsDuplicate { get; set; }
    }
}
