using System;
using System.Collections.Generic;
using System.Linq;
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
    }
}
