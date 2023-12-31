﻿using System;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Components.Operations.Factories
{
    internal class OperationFactory : IOperationFactory
    {
        public PaymentOperation Create(
            Guid paymentAccountId,
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            DateOnly operationDay)
        {
            if (!Guid.TryParse(categoryId, out var categoryGuid) || !Guid.TryParse(contractorId, out var contractorGuid))
            {
                return default;
            }

            return new PaymentOperation
            {
                Key = Guid.NewGuid(),
                OperationDay = operationDay,
                Amount = amount,
                Comment = comment,
                PaymentAccountId = paymentAccountId,
                CategoryId = categoryGuid,
                ContractorId = contractorGuid
            };
        }
    }
}
