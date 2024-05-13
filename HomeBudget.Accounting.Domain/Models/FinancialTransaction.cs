using System;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Domain.Models
{
    public class FinancialTransaction : DomainEntity
    {
        public TransactionTypes TransactionType { get; set; }
        public DateOnly OperationDay { get; set; }
        public string Comment { get; set; }
        public Guid ContractorId { get; set; }
        public Guid CategoryId { get; set; }
        public Guid PaymentAccountId { get; set; }
        public decimal Amount { get; set; }
    }
}
