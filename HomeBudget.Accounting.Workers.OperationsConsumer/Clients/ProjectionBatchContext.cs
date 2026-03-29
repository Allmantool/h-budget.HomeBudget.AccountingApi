using System.Collections.Generic;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Observability;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Clients
{
    internal sealed class ProjectionBatchContext
    {
        private ProjectionBatchContext(PaymentOperationEvent latestEvent, IReadOnlyDictionary<string, string> propagationCarrier)
        {
            LatestEvent = latestEvent;
            PropagationCarriers = new List<IReadOnlyDictionary<string, string>> { propagationCarrier };
        }

        public PaymentOperationEvent LatestEvent { get; set; }

        public List<IReadOnlyDictionary<string, string>> PropagationCarriers { get; }

        public static ProjectionBatchContext Create(ActivityEnvelope<PaymentOperationEvent> envelope)
        {
            return new ProjectionBatchContext(envelope.Item, envelope.PropagationCarrier);
        }
    }
}
