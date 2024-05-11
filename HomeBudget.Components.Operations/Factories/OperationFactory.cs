using System;

using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;

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
                return Result<PaymentOperation>.Failure($"Pls. re-check 'categoryId': {categoryId} or 'contractorId': {contractorId}");
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

            return Result<PaymentOperation>.Succeeded(payload);
        }

        public Result<PaymentOperation> CreateTransferOperation(
            Guid paymentAccountId,
            decimal amount,
            DateOnly operationDay)
        {
            var payload = new PaymentOperation
            {
                PaymentAccountId = paymentAccountId,
                OperationDay = operationDay,
                Amount = amount
            };

            return Result<PaymentOperation>.Succeeded(payload);
        }
    }
}
