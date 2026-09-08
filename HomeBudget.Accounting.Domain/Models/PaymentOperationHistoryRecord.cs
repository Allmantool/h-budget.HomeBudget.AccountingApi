namespace HomeBudget.Accounting.Domain.Models
{
    public sealed record PaymentOperationHistoryRecord
    {
        public FinancialTransaction Record { get; init; }
        public long? StreamRevision { get; init; }
        public decimal Balance { get; set; }
    }
}
