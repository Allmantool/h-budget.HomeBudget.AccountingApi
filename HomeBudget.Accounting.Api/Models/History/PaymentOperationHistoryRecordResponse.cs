namespace HomeBudget.Accounting.Api.Models.History
{
    public record PaymentOperationHistoryRecordResponse
    {
        public HistoryOperationRecordResponse Record { get; set; }
        public decimal Balance { get; set; }
    }
}
