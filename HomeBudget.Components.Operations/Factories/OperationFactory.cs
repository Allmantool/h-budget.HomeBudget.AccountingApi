using System;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Components.Operations.Factories
{
    internal class OperationFactory : IOperationFactory
    {
        public PaymentOperation Create(
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            string paymentAccountId)
        {
            if (!Guid.TryParse(categoryId, out var categoryGuid) || !Guid.TryParse(contractorId, out var contractorGuid))
            {
                return default;
            }

            return new PaymentOperation
            {
                Key = Guid.NewGuid(),
                OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Amount = amount,
                Comment = comment,
                PaymentAccountId = Guid.Parse(paymentAccountId),
                CategoryId = categoryGuid,
                ContractorId = contractorGuid
            };
        }
    }
}
