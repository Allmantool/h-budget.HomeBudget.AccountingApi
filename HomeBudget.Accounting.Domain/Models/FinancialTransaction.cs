using System;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Domain.Models
{
    public class FinancialTransaction : DomainEntity
    {
        public TransactionTypes TransactionType { get; init; }
        public DateOnly OperationDay { get; init; }
        public string Comment { get; set; }
        public Guid ContractorId { get; set; }
        public Guid CategoryId { get; init; }
        public Guid PaymentAccountId { get; init; }
        public decimal Amount { get; init; }
    }
}
