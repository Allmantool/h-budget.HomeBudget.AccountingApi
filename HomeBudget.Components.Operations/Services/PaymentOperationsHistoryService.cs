using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsHistoryService(
        IPaymentAccountDocumentClient paymentAccountDocumentClient,
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        ICategoryDocumentsClient categoryDocumentsClient)
        : IPaymentOperationsHistoryService
    {
        public async Task<Result<decimal>> SyncHistoryAsync(
            Guid paymentAccountId,
            IEnumerable<PaymentOperationEvent> eventsForAccount)
        {
            var initialBalance = await GetPaymentAccountInitialBalanceAsync(paymentAccountId.ToString());

            if (eventsForAccount.IsNullOrEmpty())
            {
                return Result<decimal>.Succeeded(default);
            }

            var validAndMostUpToDateOperations = GetValidAndMostUpToDateOperations(eventsForAccount)
                .OrderBy(ev => ev.Payload.OperationDay)
                .ThenBy(ev => ev.Payload.OperationUnixTime)
                .ToList();

            if (validAndMostUpToDateOperations.IsNullOrEmpty())
            {
                await paymentsHistoryDocumentsClient.RemoveAsync(paymentAccountId);
                return Result<decimal>.Succeeded(initialBalance);
            }

            if (validAndMostUpToDateOperations.Count == 1)
            {
                var paymentOperation = validAndMostUpToDateOperations.Single().Payload;

                var record = new PaymentOperationHistoryRecord
                {
                    Record = paymentOperation,
                    Balance = initialBalance + await CalculateIncrementAsync(paymentOperation)
                };

                await paymentsHistoryDocumentsClient.RemoveAsync(paymentAccountId);
                await paymentsHistoryDocumentsClient.InsertOneAsync(paymentAccountId, record);

                return Result<decimal>.Succeeded(record.Balance);
            }

            await InsertManyAsync(paymentAccountId, validAndMostUpToDateOperations, initialBalance);

            var mostUpToDateHistoryDocument = await paymentsHistoryDocumentsClient.GetLastAsync(paymentAccountId);
            var mostUpToDateHistoryRecord = mostUpToDateHistoryDocument?.Payload;

            return Result<decimal>.Succeeded(mostUpToDateHistoryRecord?.Balance ?? initialBalance);
        }

        private async Task<decimal> GetPaymentAccountInitialBalanceAsync(string paymentAccountId)
        {
            var paymentAccountDocumentResult = await paymentAccountDocumentClient.GetByIdAsync(paymentAccountId);
            var document = paymentAccountDocumentResult.Payload;

            return document == null ? 0 : document.Payload.InitialBalance;
        }

        private async Task InsertManyAsync(
            Guid paymentAccountId,
            IEnumerable<PaymentOperationEvent> validAndMostUpToDateOperations,
            decimal initialBalance)
        {
            var operationsHistory = new List<PaymentOperationHistoryRecord>();

            await paymentsHistoryDocumentsClient.RemoveAsync(paymentAccountId);

            foreach (var operationEvent in validAndMostUpToDateOperations.Select(r => r.Payload))
            {
                var previousRecordBalance = operationsHistory.Any()
                    ? operationsHistory[^1].Balance
                    : initialBalance;

                operationsHistory.Add(
                    new PaymentOperationHistoryRecord
                    {
                        Record = operationEvent,
                        Balance = previousRecordBalance + await CalculateIncrementAsync(operationEvent)
                    });
            }

            await paymentsHistoryDocumentsClient.RewriteAllAsync(paymentAccountId, operationsHistory);
        }

        private async Task<decimal> CalculateIncrementAsync(FinancialTransaction operation)
        {
            var categoryId = operation.CategoryId;

            if (operation.CategoryId.Equals(Guid.Empty))
            {
                return operation.Amount;
            }

            var documentResult = await categoryDocumentsClient.GetByIdAsync(categoryId);
            var documentPayload = documentResult.Payload;
            var category = documentPayload.Payload;

            return category.CategoryType == CategoryTypes.Income
                ? Math.Abs(operation.Amount)
                : -Math.Abs(operation.Amount);
        }

        private static IReadOnlyCollection<PaymentOperationEvent> GetValidAndMostUpToDateOperations(
            IEnumerable<PaymentOperationEvent> eventsForAccount)
        {
            var existedOperationEventGroups = eventsForAccount
                .GroupBy(ev => ev.Payload.Key)
                .Where(gr => gr.All(ev => ev.EventType != PaymentEventTypes.Removed))
                .Where(gr => gr.Any(ev => ev.EventType == PaymentEventTypes.Added));

            return existedOperationEventGroups
                .Select(gr => gr
                    .OrderBy(ev => ev.Payload.OperationDay)
                    .ThenBy(ev => ev.Payload.OperationUnixTime)
                    .Last())
                .ToList();
        }
    }
}
