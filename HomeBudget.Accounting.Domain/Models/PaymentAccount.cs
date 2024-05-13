using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Domain.Models
{
    public class PaymentAccount : DomainEntity
    {
        public AccountTypes Type { get; set; }
        public string Currency { get; set; }
        public decimal Balance { get; set; }
        public string Agent { get; set; }
        public string Description { get; set; }
    }
}