using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Exstensions;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Handlers
{
    internal class PaymentOperationsDeliveryHandler : IPaymentOperationsDeliveryHandler
    {
        private readonly ILogger<PaymentOperationsDeliveryHandler> _logger;
        private readonly EventStoreDbOptions _options;
        private readonly IEventStoreDbWriteClient<PaymentOperationEvent> _eventStoreDbWriteClient;

        public PaymentOperationsDeliveryHandler(
            ILogger<PaymentOperationsDeliveryHandler> logger,
            IOptions<EventStoreDbOptions> options,
            IEventStoreDbWriteClient<PaymentOperationEvent> eventStoreDbWriteClient)
        {
            _logger = logger;
            _options = options.Value;
            _eventStoreDbWriteClient = eventStoreDbWriteClient;
        }

        public async Task HandleAsync(IEnumerable<ActivityEnvelope<PaymentOperationEvent>> paymentEvents, CancellationToken cancellationToken)
        {
            if (paymentEvents.IsNullOrEmpty())
            {
                return;
            }

            try
            {
                var eventsGroupedByStream = paymentEvents
                    .GroupBy(GenerateStreamName)
                    .ToDictionary(g => g.Key, g => g.AsEnumerable());

                var sendTasks = eventsGroupedByStream.Select(async kvp =>
                {
                    var streamName = kvp.Key;
                    var streamEvents = kvp.Value.ToList();
                    var streamEventPayloads = streamEvents.Select(envelope => envelope.Item).ToList();

                    var firstEvent = streamEventPayloads.First();
                    var eventTypeTitle = $"{firstEvent.EventType}_{firstEvent.Payload.Key}";

                    try
                    {
                        var correlationId = firstEvent.Metadata.Get(EventMetadataKeys.CorrelationId);
                        var messageId = firstEvent.Metadata.Get(EventMetadataKeys.MessageId);
                        var (parentContext, links) = TraceContextPropagation.ResolveParentAndLinks(
                            streamEvents.Select(envelope => envelope.PropagationCarrier));
                        var propagationContext = TraceContextPropagation.Extract(streamEvents[0].PropagationCarrier);

                        using var activity = ActivityPropagation.StartActivity(
                            "eventstore.write",
                            ActivityKind.Producer,
                            parentContext,
                            links);
                        using var baggageScope = TraceContextPropagation.UseExtractedBaggage(propagationContext);
                        var writeStartedAt = Stopwatch.StartNew();

                        if (activity != null)
                        {
                            activity.SetCorrelationId(correlationId);
                            activity.SetTag("messaging.system", "eventstore");
                            activity.SetTag("messaging.stream", streamName);
                            activity.SetTag("messaging.event_count", streamEventPayloads.Count);
                            activity.SetTag("messaging.first_event_type", firstEvent.EventType.ToString());
                            activity.SetTag("messaging.message_id", messageId);
                        }

                        StampTraceMetadata(streamEventPayloads, activity);

                        await _eventStoreDbWriteClient.SendBatchAsync(
                            streamEventPayloads,
                            streamName,
                            eventTypeTitle,
                            cancellationToken);

                        writeStartedAt.Stop();
                        TelemetryMetrics.EventStoreWriteDurationMs.Record(
                            writeStartedAt.Elapsed.TotalMilliseconds,
                            [new("event_type", firstEvent.EventType.ToString())]);
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        activity?.AddEvent(ActivityEvents.EventStoreSend);
                    }
                    catch (Exception ex)
                    {
                        _logger.FailedToSendEventToStream(ex, streamName, ex.Message);
                    }
                });

                await Task.WhenAll(sendTasks);
            }
            catch (Exception ex)
            {
                _logger.FailedToProccessEvent(ex, nameof(PaymentOperationsDeliveryHandler), ex.Message);
            }
        }

        private static string GenerateStreamName(ActivityEnvelope<PaymentOperationEvent> paymentEvent)
        {
            var accountPerMonthIdentifier = paymentEvent.Item.Payload.GetMonthPeriodPaymentAccountIdentifier();
            return PaymentOperationNamesGenerator.GenerateForAccountMonthStream(accountPerMonthIdentifier);
        }

        private static void StampTraceMetadata(IEnumerable<PaymentOperationEvent> paymentEvents, Activity activity)
        {
            if (activity is null || paymentEvents is null)
            {
                return;
            }

            var propagationCarrier = TraceContextPropagation.Capture(activity);

            foreach (var paymentEvent in paymentEvents)
            {
                paymentEvent.Metadata[EventMetadataKeys.TraceId] = activity.TraceId.ToString();
                paymentEvent.Metadata[EventMetadataKeys.CausationId] = activity.SpanId.ToString();

                if (propagationCarrier.TryGetValue(TraceContextPropagation.TraceParent, out var traceParent))
                {
                    paymentEvent.Metadata[EventMetadataKeys.TraceParent] = traceParent;
                }

                if (propagationCarrier.TryGetValue(TraceContextPropagation.TraceState, out var traceState))
                {
                    paymentEvent.Metadata[EventMetadataKeys.TraceState] = traceState;
                }

                if (propagationCarrier.TryGetValue(TraceContextPropagation.Baggage, out var baggage))
                {
                    paymentEvent.Metadata[EventMetadataKeys.Baggage] = baggage;
                }
                else
                {
                    paymentEvent.Metadata.Remove(EventMetadataKeys.Baggage);
                }
            }
        }
    }
}
