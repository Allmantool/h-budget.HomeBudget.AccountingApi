using System;

namespace HomeBudget.Accounting.Api.Models.PaymentAccount
{
    public record PaymentAccountResponse
    {
        public Guid Key { get; set; }
        public string Agent { get; set; }
        public decimal InitialBalance { get; set; }
        public string Currency { get; set; }
        public string Description { get; set; }
        public int AccountType { get; set; }
    }
}
