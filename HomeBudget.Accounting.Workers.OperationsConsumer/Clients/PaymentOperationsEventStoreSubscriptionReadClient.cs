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
        private const string DefaultGroup = "ps-homeledger-mongo-projection-v1";

        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<PaymentOperationsEventStoreSubscriptionReadClient> _logger;
        private readonly ConcurrentDictionary<string, ProjectionBatchContext> _latestEventsPerAccount = new();
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectionLocksByAccount = new();
        private readonly Channel<ProjectionBatchContext> _paymentEventsBuffer;
        private readonly EventStoreDbOptions _opts;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processorTask;
        private readonly IEventStoreDbStreamReadClient<PaymentOperationEvent> _eventStoreDbStreamReadClient;
        private readonly string _group;

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
            _group = string.IsNullOrWhiteSpace(_opts.PaymentHistoryProjectionGroup)
                ? DefaultGroup
                : _opts.PaymentHistoryProjectionGroup;
            _paymentEventsBuffer = PaymentOperationEventChannelFactory.CreateProjectionBufferChannel(_opts);
            _processorTask = Task.Run(ProcessEventBatchAsync, _cts.Token);
            _processorTask.ContinueWith(
                t => _logger.BatchProcessorCrashed(t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public override Task CreatePersistentSubscriptionAsync(CancellationToken ct)
        {
            return CreatePersistentSubscriptionAsync(_group, ct);
        }

        public override async Task<PersistentSubscription> SubscribeAsync(
            Func<ResolvedEvent, Task> handler = null,
            CancellationToken ct = default)
        {
            if (handler is not null)
            {
                return await SubscribeAsync(_group, handler, ct);
            }

            await SubscribeStreamingAsync(_group, ct);
            return null;
        }

        protected override bool DefersAcknowledgement => true;

        protected override async Task OnEventBatchAppearedAsync(
            IReadOnlyCollection<(PaymentOperationEvent EventData, EventStoreSubscriptionContext Context)> events)
        {
            var grouped = new Dictionary<string, ProjectionBatchContext>(StringComparer.Ordinal);
            foreach (var (eventData, context) in events)
            {
                var projection = ProjectionBatchContext.Create(
                    ActivityEnvelope<PaymentOperationEvent>.Capture(eventData),
                    context);
                var periodKey = eventData.Payload.GetMonthPeriodPaymentAccountIdentifier();
                if (grouped.TryGetValue(periodKey, out var existing))
                {
                    existing.Merge(projection);
                }
                else
                {
                    grouped.Add(periodKey, projection);
                }
            }

            var batches = grouped.ToArray();
            await Parallel.ForEachAsync(
                batches,
                new ParallelOptions
                {
                    CancellationToken = _cts.Token,
                    MaxDegreeOfParallelism = Math.Max(1, _opts.RequestRateLimiter)
                },
                ProcessProjectionBatchAsync);

            var failures = batches
                .Select(static item => item.Value.ProcessingError)
                .Where(static error => error is not null)
                .ToArray();
            if (failures.Length > 0)
            {
                throw new AggregateException("One or more payment projection batches failed.", failures);
            }
        }

        protected override Task OnEventAppearedAsync(PaymentOperationEvent eventData) =>
            EnqueueProjectionAsync(eventData, null);

        protected override Task OnEventAppearedAsync(
            PaymentOperationEvent eventData,
            EventStoreSubscriptionContext context) =>
            EnqueueProjectionAsync(eventData, context);

        private async Task EnqueueProjectionAsync(
            PaymentOperationEvent eventData,
            EventStoreSubscriptionContext context)
        {
            try
            {
                await _paymentEventsBuffer.Writer.WriteAsync(
                    ProjectionBatchContext.Create(
                        ActivityEnvelope<PaymentOperationEvent>.Capture(eventData),
                        context),
                    _cts.Token);
            }
            catch (ChannelClosedException)
            {
                _logger.ChannelClosedDropping(eventData.EventType.ToString());
                if (context is not null)
                {
                    await context.RetryAsync("Projection channel is closed.");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.ChannelWriteCanceled();
                if (context is not null)
                {
                    await context.RetryAsync("Projection channel write was canceled.");
                }
            }
        }

        private async Task ProcessEventBatchAsync()
        {
            var delayMs = Math.Max(0, _opts.EventBatchingDelayInMs);

            try
            {
                while (await _paymentEventsBuffer.Reader.WaitToReadAsync(_cts.Token))
                {
                    if (delayMs > 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _cts.Token);
                    }

                    while (_paymentEventsBuffer.Reader.TryRead(out var projection))
                    {
                        var periodKey = projection.LatestEvent.Payload.GetMonthPeriodPaymentAccountIdentifier();
                        _latestEventsPerAccount.AddOrUpdate(
                            periodKey,
                            _ => projection,
                            (_, existing) =>
                            {
                                existing.Merge(projection);
                                return existing;
                            });
                    }

                    var batches = new List<KeyValuePair<string, ProjectionBatchContext>>();
                    foreach (var periodKey in _latestEventsPerAccount.Keys)
                    {
                        if (_latestEventsPerAccount.TryRemove(periodKey, out var batch))
                        {
                            batches.Add(new(periodKey, batch));
                        }
                    }

                    await Parallel.ForEachAsync(
                        batches,
                        new ParallelOptions
                        {
                            CancellationToken = _cts.Token,
                            MaxDegreeOfParallelism = Math.Max(1, _opts.RequestRateLimiter)
                        },
                        ProcessProjectionBatchAsync);

                    await SettleProjectionBatchesAsync(batches);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                CancelPendingProjections();
            }
        }

        private async ValueTask ProcessProjectionBatchAsync(
            KeyValuePair<string, ProjectionBatchContext> item,
            CancellationToken token)
        {
            var (periodKey, batch) = item;
            try
            {
                await HandlePaymentOperationEventAsync(batch, batch.SubscriptionContext, token);
            }
            catch (OperationCanceledException ex)
            {
                batch.MarkFailed(ex);
            }
            catch (Exception ex)
            {
                _logger.HandleEventsFailed(periodKey, ex);
                TelemetryMetrics.ProjectionFailures.Add(1, [new("projection_name", "sync_operations_history")]);
                batch.MarkFailed(ex);
            }
        }

        private async Task SettleProjectionBatchesAsync(
            IReadOnlyCollection<KeyValuePair<string, ProjectionBatchContext>> batches)
        {
            foreach (var (periodKey, batch) in batches)
            {
                try
                {
                    if (batch.ProcessingError is null)
                    {
                        await batch.CompleteAsync();
                    }
                    else
                    {
                        await batch.RetryAsync(batch.ProcessingError);
                    }
                }
                catch (Exception ex)
                {
                    _logger.HandleEventsFailed(periodKey, ex);
                    TelemetryMetrics.ProjectionFailures.Add(1, [new("projection_name", "sync_operations_history")]);
                    await batch.RetryAsync(ex);
                }
            }
        }

        private void CancelPendingProjections()
        {
            while (_paymentEventsBuffer.Reader.TryRead(out var projection))
            {
                _ = projection.RetryAsync(new OperationCanceledException("Projection processor stopped."));
            }

            foreach (var batch in _latestEventsPerAccount.Values)
            {
                _ = batch.RetryAsync(new OperationCanceledException("Projection processor stopped."));
            }
        }

        private async Task HandlePaymentOperationEventAsync(
            ProjectionBatchContext projectionBatch,
            EventStoreSubscriptionContext subscriptionContext,
            CancellationToken ct)
        {
            var transaction = projectionBatch.LatestEvent.Payload;
            var accountId = transaction.PaymentAccountId;

            var projectionLock = _projectionLocksByAccount.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
            await projectionLock.WaitAsync(ct);

            try
            {
                await HandlePaymentOperationEventCoreAsync(projectionBatch, subscriptionContext, ct);
            }
            finally
            {
                projectionLock.Release();
            }
        }

        private async Task HandlePaymentOperationEventCoreAsync(
            ProjectionBatchContext projectionBatch,
            EventStoreSubscriptionContext subscriptionContext,
            CancellationToken ct)
        {
            var transaction = projectionBatch.LatestEvent.Payload;
            var accountId = transaction.PaymentAccountId;
            var monthPeriodPaymentAccountIdentifier = transaction.GetMonthPeriodPaymentAccountIdentifier();

            var paymentAccountStream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(monthPeriodPaymentAccountIdentifier);

            var events = await ReadProjectionEventsAsync(paymentAccountStream, projectionBatch.LatestEvent, ct);

            if (events.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Projection stream read returned no events for stream '{paymentAccountStream}' after payment operation '{transaction.Key}' appeared.");
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
                var commandId = firstEvent?.Metadata.Get(EventMetadataKeys.CommandId);
                var traceId = firstEvent?.Metadata.Get(EventMetadataKeys.TraceId);
                var importBatchId = firstEvent?.Metadata.Get(EventMetadataKeys.ImportBatchId);
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
                using (LogContext.PushProperty("MessageId", messageId))
                using (LogContext.PushProperty("CommandId", commandId))
                using (LogContext.PushProperty("OperationId", transaction.Key))
                using (LogContext.PushProperty("PaymentAccountId", accountId))
                using (LogContext.PushProperty("StreamId", paymentAccountStream))
                using (LogContext.PushProperty("CorrelationId", correlationId))
                using (LogContext.PushProperty("TraceId", traceId))
                using (LogContext.PushProperty("ImportBatchId", importBatchId))
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
                    TelemetryMetrics.SetProjectionLagSeconds((long)(projectionDelay / 1000));
                    activity?.SetTag("projection.delay_ms", projectionDelay);
                    await SendSyncOperationsHistoryAsync(
                        accountId,
                        events,
                        new ProjectionCheckpoint
                        {
                            StreamId = subscriptionContext?.StreamId ?? paymentAccountStream,
                            Revision = subscriptionContext?.Revision,
                            Position = subscriptionContext?.Position
                        },
                        ct);

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
                _logger.SyncFailed(MaskAccountId(accountId), paymentAccountStream, ex);
                TelemetryMetrics.ProjectionFailures.Add(1, [new("projection_name", "sync_operations_history")]);
                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts.Cancel();
                _paymentEventsBuffer.Writer.TryComplete();

                try
                {
                    _processorTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch
                {
                }

                foreach (var projectionLock in _projectionLocksByAccount.Values)
                {
                    projectionLock.Dispose();
                }

                _cts.Dispose();
            }

            base.Dispose(disposing);
        }

        private static string MaskAccountId(Guid accountId)
        {
            var value = accountId.ToString("N");
            if (string.IsNullOrEmpty(value))
            {
                return "***";
            }

            var suffixLength = Math.Min(8, value.Length);
            return $"***{value[^suffixLength..]}";
        }

        private async Task SendSyncOperationsHistoryAsync(
            Guid paymentAccountId,
            IEnumerable<PaymentOperationEvent> events,
            ProjectionCheckpoint checkpoint,
            CancellationToken ct)
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            _logger.DispatchingSync(paymentAccountId.ToString(), events.Count());

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

                await sender.Send(new SyncOperationsHistoryCommand(paymentAccountId, events, checkpoint), ct);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }

        private async Task<List<PaymentOperationEvent>> ReadProjectionEventsAsync(
            string paymentAccountStream,
            PaymentOperationEvent appearedEvent,
            CancellationToken ct)
        {
            List<PaymentOperationEvent> events = null;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                events = await _eventStoreDbStreamReadClient
                    .ReadAsync(paymentAccountStream, cancellationToken: ct)
                    .ToListAsync(ct);

                if (events.Count > 0)
                {
                    return events;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }

            return appearedEvent is null
                ? events ?? []
                : [appearedEvent];
        }
    }
}
