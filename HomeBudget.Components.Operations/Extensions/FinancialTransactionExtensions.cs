using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Extensions
{
    internal static class FinancialTransactionExtensions
    {
        public static decimal CalculateIncrement(
            this FinancialTransaction operation,
            IReadOnlyDictionary<Guid, Category> categoryMap)
        {
            if (operation.CategoryId == Guid.Empty)
            {
                return operation.Amount;
            }

            if (!categoryMap.TryGetValue(operation.CategoryId, out var category))
            {
                category = new Category(CategoryTypes.Expense, ["with empty category"]);
            }

            return category.CategoryType == CategoryTypes.Income
                ? Math.Abs(operation.Amount)
                : -Math.Abs(operation.Amount);
        }
    }
}
