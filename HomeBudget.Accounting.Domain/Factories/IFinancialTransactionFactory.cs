﻿using System;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Domain.Factories
{
    public interface IFinancialTransactionFactory
    {
        Result<FinancialTransaction> CreatePayment(
            Guid paymentAccountId,
            int scopeOperationId,
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            DateOnly operationDay);

        Result<FinancialTransaction> CreateTransfer(
            Guid paymentAccountId,
            decimal amount,
            DateOnly operationDay);
    }
}
