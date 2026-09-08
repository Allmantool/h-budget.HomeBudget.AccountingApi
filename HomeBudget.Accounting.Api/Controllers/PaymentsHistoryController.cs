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
using HomeBudget.Components.Accounts.Services.Interfaces;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentsHistoryByPaymentAccountId, Name = Endpoints.PaymentsHistory)]
    [ApiController]
    public class PaymentsHistoryController(
        IPaymentHistoryQueryService paymentHistoryQueryService,
        IPaymentOperationHistoryService paymentOperationHistoryService,
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

            var paymentAccountOperations = await paymentOperationHistoryService.GetHistoryWithRunningBalancesAsync(targetAccountGuid);

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
            var query = paymentHistoryQueryService.CreateQuery(mapper.Map<PaymentHistoryQueryPayload>(request), categories);
            var result = await paymentsHistoryDocumentsClient.QueryAsync(targetAccountGuid, query, cancellationToken);
            var initialBalance = await paymentAccountService.GetInitialBalanceAsync(targetAccountGuid.ToString());
            var periodBalances = await paymentsHistoryDocumentsClient.GetAllPeriodBalancesForAccountAsync(targetAccountGuid);
            var openingBalancesByPeriod = periodBalances.ToOpeningBalancesByPeriod(initialBalance);
            var items = result.Items
                .Where(static document => document?.Payload?.Record != null)
                .Select(document => new PaymentOperationHistoryRecord
                {
                    Record = document.Payload.Record,
                    StreamRevision = document.Payload.StreamRevision,
                    Balance = openingBalancesByPeriod[document.Payload.Record.OperationDay.ToPeriodKey()] + document.Payload.Balance
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

            var operationById = (await paymentOperationHistoryService.GetHistoryWithRunningBalancesAsync(targetAccountGuid))
                .SingleOrDefault(operation => operation.Record.Key == targetOperationGuid);

            return operationById == null
                ? Result<PaymentOperationHistoryRecordResponse>.Failure($"The operation with '{operationId}' hasn't been found")
                : Result<PaymentOperationHistoryRecordResponse>.Succeeded(mapper.Map<PaymentOperationHistoryRecordResponse>(operationById));
        }
    }
}
