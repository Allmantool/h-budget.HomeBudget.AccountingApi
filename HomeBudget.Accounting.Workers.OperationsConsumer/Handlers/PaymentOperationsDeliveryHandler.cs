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

        public async Task HandleAsync(IEnumerable<PaymentOperationEvent> paymentEvents, CancellationToken cancellationToken)
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
                    var streamEvents = kvp.Value;

                    var firstEvent = streamEvents.First();
                    var eventTypeTitle = $"{firstEvent.EventType}_{firstEvent.Payload.Key}";

                    try
                    {
                        var traceParent = firstEvent.Metadata.Get(EventMetadataKeys.TraceParent);
                        var traceState = firstEvent.Metadata.Get(EventMetadataKeys.TraceState);
                        var baggage = firstEvent.Metadata.Get(EventMetadataKeys.Baggage);
                        var correlationId = firstEvent.Metadata.Get(EventMetadataKeys.CorrelationId);
                        var messageId = firstEvent.Metadata.Get(EventMetadataKeys.MessageId);
                        var links = TraceContextPropagation.CreateLinks(
                            streamEvents.Select(ev => (IReadOnlyDictionary<string, string>)TraceContextPropagation.BuildCarrier(
                                ev.Metadata.Get(EventMetadataKeys.TraceParent),
                                ev.Metadata.Get(EventMetadataKeys.TraceState),
                                ev.Metadata.Get(EventMetadataKeys.Baggage))));
                        var propagationContext = TraceContextPropagation.Extract(
                            TraceContextPropagation.BuildCarrier(traceParent, traceState, baggage));

                        using var activity = ActivityPropagation.StartActivity(
                            "eventstore.write",
                            ActivityKind.Producer,
                            propagationContext.ActivityContext,
                            links);
                        using var baggageScope = TraceContextPropagation.UseExtractedBaggage(propagationContext);
                        var writeStartedAt = Stopwatch.StartNew();

                        if (activity != null)
                        {
                            activity.SetCorrelationId(correlationId);
                            activity.SetTag("messaging.system", "eventstore");
                            activity.SetTag("messaging.stream", streamName);
                            activity.SetTag("messaging.event_count", streamEvents.Count());
                            activity.SetTag("messaging.first_event_type", firstEvent.EventType.ToString());
                            activity.SetTag("messaging.message_id", messageId);
                        }

                        await _eventStoreDbWriteClient.SendBatchAsync(
                            streamEvents,
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

        private static string GenerateStreamName(PaymentOperationEvent paymentEvent)
        {
            var accountPerMonthIdentifier = paymentEvent.Payload.GetMonthPeriodPaymentAccountIdentifier();
            return PaymentOperationNamesGenerator.GenerateForAccountMonthStream(accountPerMonthIdentifier);
        }
    }
}
