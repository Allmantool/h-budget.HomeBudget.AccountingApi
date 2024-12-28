using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Components.Accounts.Services.Interfaces;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
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
            var documents = await paymentsHistoryDocumentsClient.GetAsync(Guid.Parse(paymentAccountId));

            var initialBalance = await paymentAccountService.GetInitialBalanceAsync(paymentAccountId);

            var historyRecords = documents.Select(d => d.Payload);

            var paymentAccountOperations = historyRecords
                .GroupBy(op => op.Record.Key)
                .Select(gr => gr
                    .OrderBy(op => op.Record.OperationDay)
                    .ThenBy(op => op.Record.OperationUnixTime)
                    .Last())
                .OrderBy(r => r.Record.OperationDay)
                .ThenBy(r => r.Record.OperationUnixTime);

            var categoriesResult = await categoryDocumentsClient.GetAsync();
            var categories = categoriesResult.Payload;

            var runningBalance = initialBalance;

            foreach (var historyRecord in paymentAccountOperations)
            {
                var historyRecordCategory = categories.FirstOrDefault(c => c.Payload.Key.CompareTo(historyRecord.Record.CategoryId) == 0);

                if (historyRecordCategory == null)
                {
                    continue;
                }

                var isIncome = historyRecordCategory.Payload.CategoryType == CategoryTypes.Income;
                var operationAmount = historyRecord.Record.Amount;

                runningBalance += isIncome ? Math.Abs(operationAmount) : -Math.Abs(operationAmount);
                historyRecord.Balance = runningBalance;
            }

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

            var document = await paymentsHistoryDocumentsClient.GetByIdAsync(targetAccountGuid, targetOperationGuid);

            var operationById = document.Payload;

            return operationById == null
                ? Result<PaymentOperationHistoryRecordResponse>.Failure($"The operation with '{operationId}' hasn't been found")
                : Result<PaymentOperationHistoryRecordResponse>.Succeeded(mapper.Map<PaymentOperationHistoryRecordResponse>(operationById));
        }
    }
}
