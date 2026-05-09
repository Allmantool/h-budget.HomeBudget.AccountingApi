using System;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Factories
{
    internal class FinancialTransactionFactory : IFinancialTransactionFactory
    {
        public Result<FinancialTransaction> CreatePayment(
            Guid paymentAccountId,
            int scopeOperationId,
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            DateOnly operationDay)
        {
            var categoryIdResult = ParseOptionalReferenceId(categoryId, nameof(categoryId));
            if (!categoryIdResult.IsSucceeded)
            {
                return Result<FinancialTransaction>.Failure(categoryIdResult.StatusMessage);
            }

            var contractorIdResult = ParseOptionalReferenceId(contractorId, nameof(contractorId));
            if (!contractorIdResult.IsSucceeded)
            {
                return Result<FinancialTransaction>.Failure(contractorIdResult.StatusMessage);
            }

            var payload = new FinancialTransaction
            {
                Key = Guid.NewGuid(),
                ScopedOperationId = scopeOperationId,
                OperationDay = operationDay,
                Amount = amount,
                Comment = comment,
                PaymentAccountId = paymentAccountId,
                CategoryId = categoryIdResult.Payload,
                ContractorId = contractorIdResult.Payload,
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

        private static Result<Guid> ParseOptionalReferenceId(string referenceId, string referenceName)
        {
            if (string.IsNullOrWhiteSpace(referenceId))
            {
                return Result<Guid>.Succeeded(Guid.Empty);
            }

            return Guid.TryParse(referenceId, out var parsedReferenceId)
                ? Result<Guid>.Succeeded(parsedReferenceId)
                : Result<Guid>.Failure($"Invalid payment reference '{referenceName}' has been provided: '{referenceId}'");
        }
    }
}
