using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Observability;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Clients
{
    internal sealed class ProjectionBatchContext
    {
        private ProjectionBatchContext(
            PaymentOperationEvent latestEvent,
            IReadOnlyDictionary<string, string> propagationCarrier,
            EventStoreSubscriptionContext subscriptionContext)
        {
            LatestEvent = latestEvent;
            PropagationCarriers = new List<IReadOnlyDictionary<string, string>> { propagationCarrier };
            SubscriptionContext = subscriptionContext;
            SubscriptionContexts = subscriptionContext is null ? [] : [subscriptionContext];
        }

        public PaymentOperationEvent LatestEvent { get; private set; }

        public List<IReadOnlyDictionary<string, string>> PropagationCarriers { get; }

        public EventStoreSubscriptionContext SubscriptionContext { get; private set; }

        public Exception ProcessingError { get; private set; }

        private List<EventStoreSubscriptionContext> SubscriptionContexts { get; }

        public static ProjectionBatchContext Create(
            ActivityEnvelope<PaymentOperationEvent> envelope,
            EventStoreSubscriptionContext subscriptionContext = null)
        {
            return new ProjectionBatchContext(
                envelope.Item,
                envelope.PropagationCarrier,
                subscriptionContext);
        }

        public void Merge(ProjectionBatchContext incoming)
        {
            if (incoming.LatestEvent.SequenceNumber >= LatestEvent.SequenceNumber)
            {
                LatestEvent = incoming.LatestEvent;
                SubscriptionContext = incoming.SubscriptionContext;
            }

            PropagationCarriers.AddRange(incoming.PropagationCarriers);
            SubscriptionContexts.AddRange(incoming.SubscriptionContexts);
        }

        public async Task CompleteAsync()
        {
            foreach (var context in SubscriptionContexts)
            {
                await context.AcknowledgeAsync();
            }
        }

        public async Task RetryAsync(Exception exception)
        {
            var reason = exception?.Message ?? "Projection processing failed.";
            foreach (var context in SubscriptionContexts)
            {
                await context.RetryAsync(reason);
            }
        }

        public void MarkFailed(Exception exception)
        {
            ProcessingError = exception ?? throw new ArgumentNullException(nameof(exception));
        }
    }
}
