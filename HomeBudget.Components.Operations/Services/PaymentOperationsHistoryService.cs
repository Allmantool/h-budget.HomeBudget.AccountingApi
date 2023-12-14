using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsHistoryService : IPaymentOperationsHistoryService
    {
        public Result<decimal> SyncHistory()
        {
            if (!MockOperationEventsStore.Events.Any())
            {
                MockOperationsHistoryStore.SetState(Enumerable.Empty<PaymentOperationHistoryRecord>());

                return new Result<decimal>();
            }

            var historyEvents = MockOperationEventsStore.Events
                .GroupBy(ev => ev.PaymentOperationId).Where(gr => gr.All(ev => ev.EventType != EventTypes.Remove))
                .Select(gr => gr.OrderBy(ev => ev.OperationUnixTime).Last());

            if (historyEvents.Count() == 1)
            {
                var historyRecord = historyEvents.Single().Payload;

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

            foreach (var operationEvent in historyEvents)
            {
                var previousRecordBalance = operationsHistory.Any()
                    ? operationsHistory.Last().Value.Balance
                    : 0;

                operationsHistory.Add(
                    operationEvent.Payload.OperationUnixTime,
                    new PaymentOperationHistoryRecord
                    {
                        Record = operationEvent.Payload,
                        Balance = previousRecordBalance + operationEvent.Payload.Amount
                    });
            }

            MockOperationsHistoryStore.SetState(operationsHistory.Values);

            return new Result<decimal>(MockOperationsHistoryStore.Records.Last().Balance);
        }
    }
}
