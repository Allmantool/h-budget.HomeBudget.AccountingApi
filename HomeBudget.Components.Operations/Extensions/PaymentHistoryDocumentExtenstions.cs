using System.Collections.Generic;
using System.Linq;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Extensions
{
    public static class PaymentHistoryDocumentExtenstions
    {
        public static IReadOnlyDictionary<string, decimal> ToOpeningBalancesByPeriod(
            this IEnumerable<PaymentHistoryDocument> periodBalances,
            decimal initialBalance)
        {
            var openingBalances = new Dictionary<string, decimal>();
            var runningBalance = initialBalance;

            foreach (var periodBalance in periodBalances
                         .Where(static document => document?.Payload?.Record != null)
                         .OrderBy(document => document.Payload.Record.OperationDay.Year)
                         .ThenBy(document => document.Payload.Record.OperationDay.Month))
            {
                var periodKey = periodBalance.Payload.Record.OperationDay.ToPeriodKey();
                openingBalances[periodKey] = runningBalance;
                runningBalance += periodBalance.Payload.Balance;
            }

            return openingBalances;
        }
    }
}
