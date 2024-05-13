using System;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Factories
{
    internal class FinancialTransactionFactory : IFinancialTransactionFactory
    {
        public Result<FinancialTransaction> CreatePayment(
            Guid paymentAccountId,
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            DateOnly operationDay)
        {
            if (!Guid.TryParse(categoryId, out var categoryGuid) || !Guid.TryParse(contractorId, out var contractorGuid))
            {
                return Result<FinancialTransaction>.Failure($"Pls. re-check 'categoryId': {categoryId} or 'contractorId': {contractorId}");
            }

            var payload = new FinancialTransaction
            {
                Key = Guid.NewGuid(),
                OperationDay = operationDay,
                Amount = amount,
                Comment = comment,
                PaymentAccountId = paymentAccountId,
                CategoryId = categoryGuid,
                ContractorId = contractorGuid,
                TransactionType = TransactionTypes.Payment
            };

            return Result<FinancialTransaction>.Succeeded(payload);
        }

        public Result<FinancialTransaction> CreateTransfer(
            Guid paymentAccountId,
            decimal amount,
            DateOnly operationDay)
        {
            var payload = new FinancialTransaction
            {
                PaymentAccountId = paymentAccountId,
                OperationDay = operationDay,
                Amount = amount,
                TransactionType = TransactionTypes.Transfer
            };

            return Result<FinancialTransaction>.Succeeded(payload);
        }
    }
}
