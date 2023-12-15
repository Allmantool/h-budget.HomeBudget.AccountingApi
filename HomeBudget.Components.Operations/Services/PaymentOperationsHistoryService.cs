using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsHistoryService : IPaymentOperationsHistoryService
    {
        public Result<decimal> SyncHistory(Guid paymentAccountId)
        {
            var eventsForAccount = MockOperationEventsStore.EventsForAccount(paymentAccountId).ToList();

            if (!eventsForAccount.Any())
            {
                MockOperationsHistoryStore.SetState(paymentAccountId, Enumerable.Empty<PaymentOperationHistoryRecord>());
                return new Result<decimal>();
            }

            var validAndMostUpToDateOperations = GetValidAndMostUpToDateOperations(eventsForAccount);

            if (!validAndMostUpToDateOperations.Any())
            {
                MockOperationsHistoryStore.SetState(paymentAccountId, Enumerable.Empty<PaymentOperationHistoryRecord>());
                return new Result<decimal>();
            }

            if (validAndMostUpToDateOperations.Count == 1)
            {
                var historyRecord = validAndMostUpToDateOperations.Single().Payload;

                MockOperationsHistoryStore.SetState(
                    paymentAccountId,
                    new[]
                    {
                        new PaymentOperationHistoryRecord
                        {
                            Record = historyRecord,
                            Balance = historyRecord.Amount
                        }
                    });

                return new Result<decimal>(MockOperationsHistoryStore.Records.Last().Balance);
            }

            var operationsHistory = new SortedList<long, PaymentOperationHistoryRecord>();

            foreach (var operationEvent in validAndMostUpToDateOperations.Select(r => r.Payload))
            {
                var previousRecordBalance = operationsHistory.Any()
                    ? operationsHistory.Last().Value.Balance
                    : 0;

                operationsHistory.Add(
                    operationEvent.OperationUnixTime,
                    new PaymentOperationHistoryRecord
                    {
                        Record = operationEvent,
                        Balance = previousRecordBalance + operationEvent.Amount
                    });
            }

            MockOperationsHistoryStore.SetState(paymentAccountId, operationsHistory.Values);

            var historyOperationRecord = MockOperationsHistoryStore.Records.Any()
                ? MockOperationsHistoryStore.Records.Last()
                : default;

            return new Result<decimal>(historyOperationRecord?.Balance ?? 0);
        }

        private static IReadOnlyCollection<PaymentOperationEvent> GetValidAndMostUpToDateOperations(
            IEnumerable<PaymentOperationEvent> eventsForAccount)
        {
            var existedOperationEventGroups = eventsForAccount
                .GroupBy(ev => ev.Payload.Key)
                .Where(gr => gr.All(ev => ev.EventType != EventTypes.Remove))
                .Where(gr => gr.Any(ev => ev.EventType == EventTypes.Add));

            return existedOperationEventGroups
                .Select(gr => gr
                    .OrderBy(ev => ev.Payload.OperationDay)
                    .ThenBy(ev => ev.Payload.OperationUnixTime)
                    .Last())
                .ToList();
        }
    }
}
