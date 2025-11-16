using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EventStore.Client;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Logs;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal sealed class PaymentOperationsEventStoreClient
        : BaseEventStoreClient<PaymentOperationEvent>, IDisposable
    {
        private readonly Channel<PaymentOperationEvent> _paymentEventChannel;
        private readonly ConcurrentDictionary<string, PaymentOperationEvent> _latestEventsPerAccount = new();

        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ILogger<PaymentOperationsEventStoreClient> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly EventStoreDbOptions _opts;

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processorTask;
        private readonly SemaphoreSlim requestRateLimiter;

        public PaymentOperationsEventStoreClient(
            ILogger<PaymentOperationsEventStoreClient> logger,
            IServiceScopeFactory serviceScopeFactory,
            IDateTimeProvider dateTimeProvider,
            EventStoreClient client,
            IOptions<EventStoreDbOptions> options)
            : base(client, options.Value, logger)
        {
            _opts = options.Value;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _dateTimeProvider = dateTimeProvider;
            _paymentEventChannel = PaymentOperationEventChannelFactory.CreateChannel(_opts);
            _retryPolicy = EventStoreRetryPolicies.BuildRetryPolicy(_opts, _logger);
            requestRateLimiter = new(_opts.RequestRateLimiter);
            _processorTask = Task.Run(ProcessEventBatchAsync, _cts.Token);
            _processorTask.ContinueWith(
                t => PaymentOperationsEventStoreClientLogs.BatchProcessorCrashed(_logger, t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public override async Task<IWriteResult> SendBatchAsync(
            IEnumerable<PaymentOperationEvent> eventsForSending,
            string streamName,
            string eventType = null,
            CancellationToken ctx = default)
        {
            try
            {
                await requestRateLimiter.WaitAsync(ctx);

                return await _retryPolicy.ExecuteAsync(
                    async retryCtx => await base.SendBatchAsync(eventsForSending, streamName, eventType, ctx),
                    ctx);
            }
            catch (Exception ex)
            {
                PaymentOperationsEventStoreClientLogs.SendEventToDeadQueue(_logger, ex.Message, ex);
                await SendToDeadLetterQueueAsync(eventsForSending, ex);
                throw;
            }
            finally
            {
                requestRateLimiter.Release();
            }
        }

        public override async Task<IWriteResult> SendAsync(
            PaymentOperationEvent eventForSending,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(eventForSending);

            var ctx = new Context
            {
                [nameof(PaymentOperationEvent.EventType)] = eventType,
                [nameof(PaymentOperationEvent.Payload.Key)] = eventForSending.Payload?.Key
            };

            try
            {
                return await _retryPolicy.ExecuteAsync(
                    async retryCtx =>
                    {
                        eventForSending.Metadata ??= new Dictionary<string, string>();
                        eventForSending.Metadata["CorrelationId"] = retryCtx.CorrelationId.ToString();
                        eventForSending.Metadata["RetryCount"] = retryCtx.Count.ToString();

                        eventForSending.EnvelopId = retryCtx.CorrelationId;

                        var opKey = eventForSending.Payload?.Key.ToString();
                        PaymentOperationsEventStoreClientLogs.SendingEvent(_logger, eventType, opKey, retryCtx.CorrelationId);

                        var result = await base.SendAsync(
                            eventForSending,
                            streamName ?? string.Empty,
                            eventType ?? string.Empty,
                            token);

                        PaymentOperationsEventStoreClientLogs.EventSent(_logger, eventType, opKey, retryCtx.CorrelationId);
                        return result;
                    },
                    ctx);
            }
            catch (Exception ex)
            {
                PaymentOperationsEventStoreClientLogs.RetriesExhausted(_logger, eventType, eventForSending.Payload?.Key.ToString(), ex);
                await SendToDeadLetterQueueAsync(eventForSending, ex);
                throw;
            }
        }

        public override IAsyncEnumerable<PaymentOperationEvent> ReadAsync(
            string streamName,
            int maxEvents = int.MaxValue,
            CancellationToken token = default)
        {
            var resolvedStream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(streamName);
            return base.ReadAsync(resolvedStream, maxEvents, token);
        }

        protected override async Task OnEventAppearedAsync(PaymentOperationEvent eventData)
        {
            try
            {
                await _paymentEventChannel.Writer.WriteAsync(eventData, _cts.Token);
            }
            catch (ChannelClosedException)
            {
                PaymentOperationsEventStoreClientLogs.ChannelClosedDropping(_logger, eventData.EventType.ToString());
            }
            catch (OperationCanceledException)
            {
                PaymentOperationsEventStoreClientLogs.ChannelWriteCanceled(_logger);
            }
        }

        private async Task ProcessEventBatchAsync()
        {
            var delayMs = Math.Max(0, _opts.EventBatchingDelayInMs);

            while (await _paymentEventChannel.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_paymentEventChannel.Reader.TryRead(out var evt))
                {
                    _latestEventsPerAccount[evt.Payload.GetMonthPeriodIdentifier()] = evt;
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
                        await HandlePaymentOperationEventAsync(latestEvent.Payload, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // shutdown
                    }
                    catch (Exception ex)
                    {
                        PaymentOperationsEventStoreClientLogs.HandleEventsFailed(_logger, periodKey, ex);
                    }
                    finally
                    {
                        _latestEventsPerAccount.Remove(periodKey, out _);
                    }
                }
            }
        }

        private async Task HandlePaymentOperationEventAsync(FinancialTransaction transaction, CancellationToken ct)
        {
            var accountId = transaction.PaymentAccountId;
            var periodKey = transaction.GetMonthPeriodIdentifier();

            var events = await ReadAsync(periodKey, token: ct).ToListAsync(ct);

            foreach (var e in events)
            {
                if (e.ProcessedAt == default)
                {
                    e.ProcessedAt = _dateTimeProvider.GetNowUtc();
                }
            }

            try
            {
                await SendSyncOperationsHistoryAsync(accountId, events, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                PaymentOperationsEventStoreClientLogs.SyncFailed(_logger, accountId, periodKey, ex);
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

            PaymentOperationsEventStoreClientLogs.DispatchingSync(_logger, paymentAccountId, events.Count());
            await sender.Send(new SyncOperationsHistoryCommand(paymentAccountId, events), ct);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _paymentEventChannel.Writer.TryComplete();

            try
            {
                _processorTask?.Wait(TimeSpan.FromSeconds(5));
                requestRateLimiter.Dispose();

                base.Dispose();
            }
            catch
            {
            }

            _cts.Dispose();
        }
    }
}
