namespace HomeBudget.Accounting.Domain.Models
{
    public class PaymentOperationHistoryRecord
    {
        public FinancialTransaction Record { get; init; }
        public decimal Balance { get; set; }
    }
}
