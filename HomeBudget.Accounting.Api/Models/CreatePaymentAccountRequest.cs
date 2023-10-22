using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.Models
{
    public class CreatePaymentAccountRequest
    {
        public string Agent { get; set; }
        public decimal Balance { get; set; }
        public string Currency { get; set; }
        public string Description { get; set; }
        public AccountTypes AccountType { get; set; }
    }
}
