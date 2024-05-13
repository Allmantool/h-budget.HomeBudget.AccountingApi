namespace HomeBudget.Accounting.Api.Models.PaymentAccount
{
    public class CreatePaymentAccountRequest
    {
        public string Agent { get; set; }
        public decimal InitialBalance { get; set; }
        public string Currency { get; set; }
        public string Description { get; set; }
        public int AccountType { get; set; }
    }
}
