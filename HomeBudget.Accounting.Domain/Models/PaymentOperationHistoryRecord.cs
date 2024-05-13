namespace HomeBudget.Accounting.Domain.Models
{
    public class PaymentOperationHistoryRecord
    {
        public FinancialTransaction Record { get; set; }
        public decimal Balance { get; set; }
    }
}
