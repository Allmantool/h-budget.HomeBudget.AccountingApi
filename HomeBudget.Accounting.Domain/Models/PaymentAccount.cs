﻿using System;

namespace HomeBudget.Accounting.Domain.Models
{
    public class PaymentAccount
    {
        public Guid Id { get; set; }
        public AccountTypes Type { get; set; }
        public string Currency { get; set; }
        public decimal Balance { get; set; }
        public string Agent { get; set; }
        public string Description { get; set; }
    }
}