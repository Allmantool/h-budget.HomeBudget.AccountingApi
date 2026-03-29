using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EventStore.Client;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Factories;
using HomeBudget.Accounting.Workers.OperationsConsumer.Logs;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Exstensions;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Clients
{
    internal sealed class PaymentOperationsEventStoreSubscriptionReadClient
        : BaseEventStoreSubscriptionReadClient<PaymentOperationEvent>
    {
        private const string Group = "ps-homeledger-mongo-projection-v1";

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<PaymentOperationsEventStoreSubscriptionReadClient> _logger;
        private readonly ConcurrentDictionary<string, ProjectionBatchContext> _latestEventsPerAccount = new();
        private readonly Channel<ActivityEnvelope<PaymentOperationEvent>> _paymentEventsBuffer;
        private readonly EventStoreDbOptions _opts;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processorTask;
        private readonly IEventStoreDbStreamReadClient<PaymentOperationEvent> _eventStoreDbStreamReadClient;

        public PaymentOperationsEventStoreSubscriptionReadClient(
            ILogger<PaymentOperationsEventStoreSubscriptionReadClient> logger,
            IEventStoreDbStreamReadClient<PaymentOperationEvent> eventStoreDbStreamReadClient,
            IServiceScopeFactory serviceScopeFactory,
            EventStorePersistentSubscriptionsClient client,
            IDateTimeProvider dateTimeProvider,
            IOptions<EventStoreDbOptions> options)
            : base(client, options.Value, logger)
        {
            _eventStoreDbStreamReadClient = eventStoreDbStreamReadClient;
            _dateTimeProvider = dateTimeProvider;
            _opts = options.Value;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _paymentEventsBuffer = PaymentOperationEventChannelFactory.CreateBufferChannel(_opts);
            _processorTask = Task.Run(ProcessEventBatchAsync, _cts.Token);
            _processorTask.ContinueWith(
                t => _logger.BatchProcessorCrashed(t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public override Task CreatePersistentSubscriptionAsync(CancellationToken ct)
        {
            return CreatePersistentSubscriptionAsync(Group, ct);
        }

        public override Task<PersistentSubscription> SubscribeAsync(
            Func<ResolvedEvent, Task> handler = null,
            CancellationToken ct = default)
        {
            return SubscribeAsync(Group, handler, ct);
        }

        protected override async Task OnEventAppearedAsync(PaymentOperationEvent eventData)
        {
            try
            {
                await _paymentEventsBuffer.Writer.WriteAsync(
                    ActivityEnvelope<PaymentOperationEvent>.Capture(eventData),
                    _cts.Token);
            }
            catch (ChannelClosedException)
            {
                _logger.ChannelClosedDropping(eventData.EventType.ToString());
            }
            catch (OperationCanceledException)
            {
                _logger.ChannelWriteCanceled();
            }
        }

        private async Task ProcessEventBatchAsync()
        {
            var delayMs = Math.Max(0, _opts.EventBatchingDelayInMs);

            while (await _paymentEventsBuffer.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_paymentEventsBuffer.Reader.TryRead(out var evt))
                {
                    var periodKey = evt.Item.Payload.GetMonthPeriodPaymentAccountIdentifier();
                    _latestEventsPerAccount.AddOrUpdate(
                        periodKey,
                        _ => ProjectionBatchContext.Create(evt),
                        (_, existing) =>
                        {
                            existing.LatestEvent = evt.Item;
                            existing.PropagationCarriers.Add(evt.PropagationCarrier);
                            return existing;
                        });
                }

                if (delayMs > 0)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                foreach (var (periodKey, latestEvent) in _latestEventsPerAccount.ToArray())
                {
                    try
                    {
                        await HandlePaymentOperationEventAsync(latestEvent, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // shutdown
                    }
                    catch (Exception ex)
                    {
                        _logger.HandleEventsFailed(periodKey, ex);
                    }
                    finally
                    {
                        _latestEventsPerAccount.Remove(periodKey, out _);
                    }
                }
            }
        }

        private async Task HandlePaymentOperationEventAsync(ProjectionBatchContext projectionBatch, CancellationToken ct)
        {
            var transaction = projectionBatch.LatestEvent.Payload;
            var accountId = transaction.PaymentAccountId;
            var monthPeriodPaymentAccountIdentifier = transaction.GetMonthPeriodPaymentAccountIdentifier();

            var paymentAccountStream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(monthPeriodPaymentAccountIdentifier);

            var events = await _eventStoreDbStreamReadClient
                .ReadAsync(paymentAccountStream, cancellationToken: ct)
                .ToListAsync(ct);

            if (events.Count == 0)
            {
                return;
            }

            foreach (var e in events)
            {
                if (e.ProcessedAt == default)
                {
                    e.ProcessedAt = _dateTimeProvider.GetNowUtc();
                }
            }

            try
            {
                var firstEvent = events.FirstOrDefault();
                var correlationId = firstEvent?.Metadata.Get(EventMetadataKeys.CorrelationId);
                var traceParent = firstEvent?.Metadata.Get(EventMetadataKeys.TraceParent);
                var traceState = firstEvent?.Metadata.Get(EventMetadataKeys.TraceState);
                var baggage = firstEvent?.Metadata.Get(EventMetadataKeys.Baggage);
                var messageId = firstEvent?.Metadata.Get(EventMetadataKeys.MessageId);
                var propagationContext = TraceContextPropagation.Extract(
                    projectionBatch.PropagationCarriers.FirstOrDefault()
                    ?? TraceContextPropagation.BuildCarrier(traceParent, traceState, baggage));
                var storedEventContexts = TraceContextPropagation.ExtractContexts(
                    events.Select(ev => (IReadOnlyDictionary<string, string>)TraceContextPropagation.BuildCarrier(
                        ev.Metadata.Get(EventMetadataKeys.TraceParent),
                        ev.Metadata.Get(EventMetadataKeys.TraceState),
                        ev.Metadata.Get(EventMetadataKeys.Baggage))));
                var consumedEventContexts = TraceContextPropagation.ExtractContexts(projectionBatch.PropagationCarriers);
                var parentContext = consumedEventContexts.FirstOrDefault();

                if (parentContext == default)
                {
                    parentContext = storedEventContexts.FirstOrDefault();
                }

                var links = consumedEventContexts
                    .Concat(storedEventContexts)
                    .Where(context => context != default && context != parentContext)
                    .Distinct()
                    .Select(context => new ActivityLink(context))
                    .ToArray();

                using (LogContext.PushProperty(EventMetadataKeys.CorrelationId, correlationId))
                using (LogContext.PushProperty(EventMetadataKeys.MessageId, messageId))
                using (LogContext.PushProperty("projection_name", "sync_operations_history"))
                using (LogContext.PushProperty("stream_id", paymentAccountStream))
                using (LogContext.PushProperty("aggregate_id", accountId))
                {
                    using var activity = ActivityPropagation.StartActivity(
                        "projection.sync_operations_history",
                        ActivityKind.Internal,
                        parentContext,
                        links);
                    using var baggageScope = TraceContextPropagation.UseExtractedBaggage(propagationContext);
                    var syncStopwatch = Stopwatch.StartNew();

                    if (activity != null)
                    {
                        activity.SetCorrelationId(correlationId);
                        activity.SetTag("messaging.system", "eventstore");
                        activity.SetTag("messaging.stream", paymentAccountStream);
                        activity.SetTag("messaging.event_count", events.Count);
                        activity.SetTag("messaging.message_id", messageId);
                        activity.SetTag("projection.name", "sync_operations_history");
                        activity.SetAccount(accountId);
                    }

                    var oldestOccurredOn = events.Min(ev => ev.OccurredOn);
                    var projectionDelay = (_dateTimeProvider.GetNowUtc() - oldestOccurredOn).TotalMilliseconds;
                    TelemetryMetrics.ProjectionDelayMs.Record(
                        projectionDelay,
                        [new("projection_name", "sync_operations_history")]);
                    activity?.SetTag("projection.delay_ms", projectionDelay);
                    await SendSyncOperationsHistoryAsync(accountId, events, ct);

                    syncStopwatch.Stop();
                    TelemetryMetrics.ProjectionSyncDurationMs.Record(
                        syncStopwatch.Elapsed.TotalMilliseconds,
                        [new("projection_name", "sync_operations_history")]);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    activity?.AddEvent(new("payment.sync.operation.send"));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.SyncFailed(accountId, paymentAccountStream, ex);
                throw;
            }
        }

        private async Task SendSyncOperationsHistoryAsync(
            Guid paymentAccountId,
            IEnumerable<PaymentOperationEvent> events,
            CancellationToken ct)
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            _logger.DispatchingSync(paymentAccountId, events.Count());

            using (LogContext.PushProperty(EventMetadataKeys.CorrelationId, events.FirstOrDefault()?.Metadata.Get(EventMetadataKeys.CorrelationId)))
            using (var activity = ActivityPropagation.StartActivity("mediatr.send.sync_operations_history", ActivityKind.Internal))
            {
                if (activity != null && !events.IsNullOrEmpty())
                {
                    var firstEvent = events.FirstOrDefault();
                    activity.SetCorrelationId(firstEvent?.Metadata.Get(EventMetadataKeys.CorrelationId));
                    activity.SetTag("messaging.system", "eventstore");
                    activity.SetTag("messaging.event_count", events.Count());
                    activity.SetTag("messaging.message_id", firstEvent?.Metadata.Get(EventMetadataKeys.MessageId));
                    activity.SetTag("projection.name", "sync_operations_history");
                    activity.SetAccount(paymentAccountId);
                }

                await sender.Send(new SyncOperationsHistoryCommand(paymentAccountId, events), ct);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }

        private sealed class ProjectionBatchContext
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
}
