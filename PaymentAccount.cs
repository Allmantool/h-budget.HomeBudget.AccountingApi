namespace HomeBudget_Accounting_Api
{
    public class PaymentAccount
    {
        public Guid Id { get; set; }

        public AccountType Type { get; set; }

        public decimal Balance { get; set; }

        public string Currency { get; set; }
        public string Agent { get; set; }

        public string Description { get; set; }
    }
}