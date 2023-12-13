using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsHistoryService : IPaymentOperationsHistoryService
    {
        public Result<decimal> SyncHistory()
        {
            if (!MockOperationEventsStore.Events.Any())
            {
                MockOperationHistoryWithBalanceStore.SetState(Enumerable.Empty<PaymentOperationHistoryRecord>());

                return new Result<decimal>();
            }

            if (MockOperationEventsStore.Events.Count == 1)
            {
                var historyRecord = MockOperationEventsStore.Events.Single().Payload;

                MockOperationHistoryWithBalanceStore.SetState(new[]
                {
                    new PaymentOperationHistoryRecord
                    {
                        Record = historyRecord,
                        Balance = historyRecord.Amount
                    }
                });

                return new Result<decimal>(MockOperationHistoryWithBalanceStore.Records.Last().Balance);
            }

            var operationsHistory = new SortedList<long, PaymentOperationHistoryRecord>();

            foreach (var operationEvent in MockOperationEventsStore.Events)
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

            MockOperationHistoryWithBalanceStore.SetState(operationsHistory.Values);

            return new Result<decimal>(MockOperationHistoryWithBalanceStore.Records.Last().Balance);
        }
    }
}
