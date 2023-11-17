using System;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Components.Operations.Factories
{
    internal class OperationFactory : IOperationFactory
    {
        public DepositOperation Create(decimal amount, string comment, string categoryId, string contractorId)
        {
            if (!Guid.TryParse(categoryId, out var categoryGuid) || !Guid.TryParse(contractorId, out var contractorGuid))
            {
                return default;
            }

            return new DepositOperation
            {
                Key = Guid.NewGuid(),
                OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Amount = amount,
                Comment = comment,
                CategoryId = categoryGuid,
                ContractorId = contractorGuid
            };
        }
    }
}
