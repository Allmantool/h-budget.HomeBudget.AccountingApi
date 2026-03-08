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
                        var traceId = firstEvent.Metadata.Get(EventMetadataKeys.TraceId);
                        var correlationId = firstEvent.Metadata.Get(EventMetadataKeys.CorrelationId);

                        using var activity = Telemetry.ActivitySource.StartActivity(
                            "eventstore.write",
                            ActivityKind.Internal,
                            traceParent);

                        if (activity != null)
                        {
                            activity.SetCorrelationId(correlationId);
                            if (!string.IsNullOrWhiteSpace(traceId))
                            {
                                activity.SetTraceId(traceId);
                            }

                            activity.SetTag("messaging.system", "eventstore");
                            activity.SetTag("messaging.stream", streamName);
                            activity.SetTag("messaging.event_count", streamEvents.Count());
                            activity.SetTag("messaging.first_event_type", firstEvent.EventType.ToString());
                        }

                        await _eventStoreDbWriteClient.SendBatchAsync(
                            streamEvents,
                            streamName,
                            eventTypeTitle,
                            cancellationToken);

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
