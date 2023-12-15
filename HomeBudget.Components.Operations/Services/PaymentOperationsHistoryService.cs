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
            var historyEventsForAccount = MockOperationEventsStore.Events
                .Where(ev => ev.Payload.PaymentAccountId.CompareTo(paymentAccountId) == 0);

            if (!historyEventsForAccount.Any())
            {
                MockOperationsHistoryStore.SetState(Enumerable.Empty<PaymentOperationHistoryRecord>());

                return new Result<decimal>();
            }

            var mostUpToDateHistoryRecords = historyEventsForAccount
                .GroupBy(ev => ev.Payload.Key)
                .Where(gr => gr.All(ev => ev.EventType != EventTypes.Remove))
                .Where(gr => gr.Any(ev => ev.EventType == EventTypes.Add))
                .Select(gr => gr
                    .OrderBy(ev => ev.Payload.OperationDay)
                    .ThenBy(ev => ev.Payload.OperationUnixTime)
                    .Last());

            if (mostUpToDateHistoryRecords.Count() == 1)
            {
                var historyRecord = mostUpToDateHistoryRecords.Single().Payload;

                MockOperationsHistoryStore.SetState(new[]
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

            var previousRecordBalance = operationsHistory.Any()
                ? operationsHistory.Last().Value.Balance
                : 0;

            foreach (var operationEvent in mostUpToDateHistoryRecords.Select(r => r.Payload))
            {
                operationsHistory.Add(
                    operationEvent.OperationUnixTime,
                    new PaymentOperationHistoryRecord
                    {
                        Record = operationEvent,
                        Balance = previousRecordBalance + operationEvent.Amount
                    });
            }

            MockOperationsHistoryStore.SetState(operationsHistory.Values);

            var historyOperationRecord = MockOperationsHistoryStore.Records.Any()
                ? MockOperationsHistoryStore.Records.Last()
                : default;

            return new Result<decimal>(historyOperationRecord?.Balance ?? 0);
        }
    }
}
