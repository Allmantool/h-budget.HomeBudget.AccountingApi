using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Components.Accounts.Services.Interfaces;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentsHistoryByPaymentAccountId, Name = Endpoints.PaymentsHistory)]
    [ApiController]
    public class PaymentsHistoryController(
        IPaymentAccountService paymentAccountService,
        ICategoryDocumentsClient categoryDocumentsClient,
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        IMapper mapper)
        : ControllerBase
    {
        [HttpGet]
        [Obsolete("Use the bounded query endpoint.")]
        public async Task<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>> GetHistoryPaymentOperationsAsync(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var paymentAccountOperations = await GetHistoryWithRunningBalancesAsync(targetAccountGuid);

            var responsePayload = mapper.Map<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>(paymentAccountOperations);

            return Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>.Succeeded(responsePayload);
        }

        [HttpGet("~/payments-history/query/{paymentAccountId}")]
        public async Task<Result<PaymentHistoryPageResponse>> QueryHistoryPaymentOperationsAsync(
            string paymentAccountId,
            [FromQuery] PaymentHistoryQueryRequest request,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<PaymentHistoryPageResponse>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var categoriesResult = await categoryDocumentsClient.GetAsync();
            var categories = categoriesResult.Payload ?? Array.Empty<CategoryDocument>();
            var query = CreateQuery(request, categories);
            var result = await paymentsHistoryDocumentsClient.QueryAsync(targetAccountGuid, query, cancellationToken);
            var initialBalance = await paymentAccountService.GetInitialBalanceAsync(targetAccountGuid.ToString());
            var periodBalances = await paymentsHistoryDocumentsClient.GetAllPeriodBalancesForAccountAsync(targetAccountGuid);
            var openingBalancesByPeriod = BuildOpeningBalancesByPeriod(periodBalances, initialBalance);
            var items = result.Items
                .Where(static document => document?.Payload?.Record != null)
                .Select(document => new PaymentOperationHistoryRecord
                {
                    Record = document.Payload.Record,
                    StreamRevision = document.Payload.StreamRevision,
                    Balance = openingBalancesByPeriod[ToPeriodKey(document.Payload.Record.OperationDay)] + document.Payload.Balance
                })
                .ToArray();
            var totalPages = result.TotalCount == 0
                ? 0
                : checked((int)Math.Ceiling(result.TotalCount / (double)query.PageSize));

            return Result<PaymentHistoryPageResponse>.Succeeded(new PaymentHistoryPageResponse
            {
                Items = mapper.Map<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>(items),
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = result.TotalCount,
                TotalPages = totalPages,
                HasPreviousPage = query.Page > 1 && result.TotalCount > 0,
                HasNextPage = query.Page < totalPages
            });
        }

        [HttpGet("byId/{operationId}")]
        public async Task<Result<PaymentOperationHistoryRecordResponse>> GetOperationByIdAsync(string paymentAccountId, string operationId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<PaymentOperationHistoryRecordResponse>.Failure($"Invalid payment account '{nameof(targetAccountGuid)}' has been provided");
            }

            if (!Guid.TryParse(operationId, out var targetOperationGuid))
            {
                return Result<PaymentOperationHistoryRecordResponse>.Failure($"Invalid payment operation '{nameof(targetOperationGuid)}' has been provided");
            }

            var operationById = (await GetHistoryWithRunningBalancesAsync(targetAccountGuid))
                .SingleOrDefault(operation => operation.Record.Key == targetOperationGuid);

            return operationById == null
                ? Result<PaymentOperationHistoryRecordResponse>.Failure($"The operation with '{operationId}' hasn't been found")
                : Result<PaymentOperationHistoryRecordResponse>.Succeeded(mapper.Map<PaymentOperationHistoryRecordResponse>(operationById));
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecord>> GetHistoryWithRunningBalancesAsync(Guid paymentAccountId)
        {
            var documents = await paymentsHistoryDocumentsClient.GetAsync(paymentAccountId);
            var initialBalance = await paymentAccountService.GetInitialBalanceAsync(paymentAccountId.ToString());
            var categoriesResult = await categoryDocumentsClient.GetAsync();
            var categories = categoriesResult.Payload ?? Array.Empty<CategoryDocument>();
            var categoryMap = categories
                .Where(category => category?.Payload != null)
                .GroupBy(category => category.Payload.Key)
                .ToDictionary(group => group.Key, group => group.Last().Payload);
            var paymentAccountOperations = documents
                .Select(document => document.Payload)
                .OrderByHistoryOrder()
                .ToArray();

            var runningBalance = initialBalance;

            foreach (var operation in paymentAccountOperations)
            {
                runningBalance += operation.Record.CalculateIncrement(categoryMap);
                operation.Balance = runningBalance;
            }

            return paymentAccountOperations;
        }

        private static PaymentHistoryQuery CreateQuery(
            PaymentHistoryQueryRequest request,
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
            PaymentHistoryQueryRequest request,
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

        private static IReadOnlyDictionary<string, decimal> BuildOpeningBalancesByPeriod(
            IEnumerable<PaymentHistoryDocument> periodBalances,
            decimal initialBalance)
        {
            var openingBalances = new Dictionary<string, decimal>();
            var runningBalance = initialBalance;
            foreach (var periodBalance in periodBalances
                         .Where(static document => document?.Payload?.Record != null)
                         .OrderBy(document => document.Payload.Record.OperationDay.Year)
                         .ThenBy(document => document.Payload.Record.OperationDay.Month))
            {
                var periodKey = ToPeriodKey(periodBalance.Payload.Record.OperationDay);
                openingBalances[periodKey] = runningBalance;
                runningBalance += periodBalance.Payload.Balance;
            }

            return openingBalances;
        }

        private static string ToPeriodKey(DateOnly operationDay)
        {
            return $"{operationDay.Year:D4}-{operationDay.Month:D2}";
        }
    }
}
