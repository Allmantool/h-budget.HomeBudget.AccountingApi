using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Domain.Models
{
    public class PaymentAccount : DomainEntity
    {
        public AccountTypes Type { get; init; }
        public string Currency { get; init; }
        public decimal Balance { get; set; }
        public string Agent { get; init; }
        public string Description { get; init; }
        public decimal InitialBalance { get; init; }
    }
}