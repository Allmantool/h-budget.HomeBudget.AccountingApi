using System;

namespace HomeBudget.Accounting.Domain.Models
{
    public class TransferOperation : DomainEntity
    {
        public DateOnly OperationDay { get; set; }
        public Guid PaymentAccountId { get; set; }
        public decimal Amount { get; set; }
    }
}
