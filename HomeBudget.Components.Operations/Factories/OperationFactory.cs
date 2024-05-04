﻿using System;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Components.Operations.Factories
{
    internal class OperationFactory : IOperationFactory
    {
        public Result<PaymentOperation> CreatePaymentOperation(
            Guid paymentAccountId,
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            DateOnly operationDay)
        {
            if (!Guid.TryParse(categoryId, out var categoryGuid) || !Guid.TryParse(contractorId, out var contractorGuid))
            {
                return new Result<PaymentOperation>(
                    isSucceeded: false,
                    message: $"Pls. re-check 'categoryId': {categoryId} or 'contractorId': {contractorId}");
            }

            var payload = new PaymentOperation
            {
                Key = Guid.NewGuid(),
                OperationDay = operationDay,
                Amount = amount,
                Comment = comment,
                PaymentAccountId = paymentAccountId,
                CategoryId = categoryGuid,
                ContractorId = contractorGuid
            };

            return new Result<PaymentOperation>(payload);
        }

        public Result<TransferOperation> CreateTransferOperation(
            Guid paymentAccountId,
            decimal amount,
            DateOnly operationDay)
        {
            var payload = new TransferOperation
            {
                Key = Guid.NewGuid(),
                OperationDay = operationDay,
                Amount = amount,
                PaymentAccountId = paymentAccountId,
            };

            return new Result<TransferOperation>(payload);
        }
    }
}
