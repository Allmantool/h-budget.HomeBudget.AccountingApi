using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentHistoryQueryService : IPaymentHistoryQueryService
    {
        public PaymentHistoryQuery CreateQuery(
            PaymentHistoryQueryPayload request,
            IReadOnlyCollection<CategoryDocument> categories)
        {
            var requestedCategoryIds = ParseCategoryIds(request, categories);

            return new PaymentHistoryQuery
            {
                Page = request.Page,
                PageSize = request.PageSize,
                SortBy = string.Equals(request.SortBy, "amount", StringComparison.OrdinalIgnoreCase)
                    ? PaymentHistorySortField.Amount
                    : PaymentHistorySortField.Date,
                SortDirection = string.Equals(request.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
                    ? PaymentHistorySortDirection.Asc
                    : PaymentHistorySortDirection.Desc,
                DateFrom = request.DateFrom,
                DateTo = request.DateTo,
                CategoryIds = requestedCategoryIds,
                HasCategoryFilter = !string.IsNullOrWhiteSpace(request.Type) || !string.IsNullOrWhiteSpace(request.CategoryId),
                ContractorId = ParseOptionalGuid(request.ContractorId),
                AmountMin = request.AmountMin,
                AmountMax = request.AmountMax
            };
        }

        private static IReadOnlyCollection<Guid> ParseCategoryIds(
            PaymentHistoryQueryPayload request,
            IReadOnlyCollection<CategoryDocument> categories)
        {
            var hasCategoryFilter = Guid.TryParse(request.CategoryId, out var categoryId);
            if (string.IsNullOrWhiteSpace(request.Type))
            {
                return hasCategoryFilter ? [categoryId] : Array.Empty<Guid>();
            }

            var targetType = string.Equals(request.Type, "income", StringComparison.OrdinalIgnoreCase)
                ? CategoryTypes.Income.Key
                : CategoryTypes.Expense.Key;
            var typeCategoryIds = categories
                .Where(category => category?.Payload?.CategoryType?.Key == targetType)
                .Select(category => category.Payload.Key)
                .ToArray();

            return hasCategoryFilter
                ? typeCategoryIds.Where(id => id == categoryId).ToArray()
                : typeCategoryIds;
        }

        private static Guid? ParseOptionalGuid(string value)
        {
            return Guid.TryParse(value, out var parsed) ? parsed : null;
        }
    }
}
