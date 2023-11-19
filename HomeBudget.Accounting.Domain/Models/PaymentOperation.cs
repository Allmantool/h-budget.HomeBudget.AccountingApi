using System;

namespace HomeBudget.Accounting.Domain.Models
{
    public class PaymentOperation : DomainEntity
    {
        public DateOnly OperationDate { get; set; }
        public string Comment { get; set; }
        public Guid ContractorId { get; set; }
        public Guid CategoryId { get; set; }
        public Guid PaymentAccountId { get; set; }
        public decimal Amount { get; set; }
    }
}
