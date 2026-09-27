using System.Collections.Generic;
using System.Linq;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Clients
{
    internal static class ProjectionStreamReadValidator
    {
        public static bool IsComplete(
            IReadOnlyCollection<PaymentOperationEvent> events,
            long requiredRevision)
        {
            if (events is null || events.Count == 0 || requiredRevision < 0)
            {
                return false;
            }

            var revisions = events
                .Select(static operation => operation.SequenceNumber)
                .Distinct()
                .Order()
                .ToArray();
            return revisions[0] == 0 &&
                revisions[^1] >= requiredRevision &&
                revisions.Length == revisions[^1] + 1;
        }
    }
}
