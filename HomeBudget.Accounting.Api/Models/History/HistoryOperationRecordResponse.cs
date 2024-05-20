using System;

namespace HomeBudget.Accounting.Api.Models.History
{
    public record HistoryOperationRecordResponse
    {
        public Guid Key { get; set; }
        public int TransactionType { get; set; }
        public DateOnly OperationDay { get; set; }
        public string Comment { get; set; }
        public Guid ContractorId { get; set; }
        public Guid CategoryId { get; set; }
        public Guid PaymentAccountId { get; set; }
        public decimal Amount { get; set; }
    }
}
