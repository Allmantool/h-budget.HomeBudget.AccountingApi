namespace HomeBudget.Accounting.Api.Models.History
{
    public record PaymentOperationHistoryRecordResponse
    {
        public HistoryOperationRecordResponse Record { get; init; }
        public decimal Balance { get; init; }
    }
}
