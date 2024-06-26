﻿namespace HomeBudget.Accounting.Api.Models.PaymentAccount
{
    public class UpdatePaymentAccountRequest
    {
        public string Agent { get; set; }
        public decimal Balance { get; set; }
        public string Currency { get; set; }
        public string Description { get; set; }
        public int AccountType { get; set; }
    }
}
