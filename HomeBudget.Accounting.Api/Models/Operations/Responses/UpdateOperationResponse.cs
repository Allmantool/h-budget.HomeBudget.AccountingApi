﻿namespace HomeBudget.Accounting.Api.Models.Operations.Responses
{
    public class UpdateOperationResponse
    {
        public string PaymentAccountId { get; set; }
        public string PaymentOperationId { get; set; }
        public decimal PaymentAccountBalance { get; set; }
    }
}
