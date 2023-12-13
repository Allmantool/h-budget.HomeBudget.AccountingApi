namespace HomeBudget.Accounting.Domain.Models
{
    public class PaymentOperationHistoryRecord
    {
        public PaymentOperation Record { get; set; }
        public decimal Balance { get; set; }
    }
}
