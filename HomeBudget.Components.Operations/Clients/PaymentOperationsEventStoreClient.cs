using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using EventStore.Client;
using Grpc.Core;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsEventStoreClient : BaseEventStoreClient<PaymentOperationEvent>
    {
        private readonly Channel<PaymentOperationEvent> _paymentEventChannel = Channel.CreateUnbounded<PaymentOperationEvent>();
        private readonly ConcurrentDictionary<string, PaymentOperationEvent> _latestEventsPerAccount = new();

        private readonly AsyncRetryPolicy _retryPolicy;

        private readonly ILogger<PaymentOperationsEventStoreClient> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly EventStoreDbOptions _eventStoreDbOptions;

        public PaymentOperationsEventStoreClient(
            ILogger<PaymentOperationsEventStoreClient> logger,
            IServiceScopeFactory serviceScopeFactory,
            EventStoreClient client,
            IOptions<EventStoreDbOptions> options)
            : base(client, options.Value, logger)
        {
            _eventStoreDbOptions = options.Value;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _retryPolicy = Policy
                .Handle<RpcException>(ex => ex.StatusCode == StatusCode.DeadlineExceeded)
                .WaitAndRetryAsync(
                    retryCount: _eventStoreDbOptions.RetryAttempts,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryAttempt, context) =>
                    {
                        var eventName = context[nameof(PaymentOperationEvent.EventType)] as string;
                        var eventKey = context[nameof(PaymentOperationEvent.EventType)] as string;

                        logger.LogWarning(
                            "Retry attempt '{RetryAttempt}' for event '{EventName}' failed. Waiting '{RetryDelay}' before next attempt. " +
                            "Event key: {EventKey}" +
                            "Exception: {Exception}",
                            retryAttempt,
                            eventName,
                            timeSpan,
                            eventKey,
                            exception.Message);
                    });

            _ = Task.Run(ProcessEventBatchAsync);
        }

        public override async Task<IWriteResult> SendAsync(
            PaymentOperationEvent eventForSending,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            var context = new Context
            {
                [nameof(PaymentOperationEvent.EventType)] = eventType,
                [nameof(PaymentOperationEvent.Payload.Key)] = eventForSending.Payload?.Key
            };

            return await _retryPolicy.ExecuteAsync(
                async retryPolicyCtx =>
                {
                    try
                    {
                        eventForSending.Metadata = new Dictionary<string, string>
                        {
                            { nameof(retryPolicyCtx.CorrelationId), retryPolicyCtx.CorrelationId.ToString() },
                            { nameof(retryPolicyCtx.Count), retryPolicyCtx.Count.ToString() },
                            { nameof(retryPolicyCtx.Values), string.Join(",", retryPolicyCtx.Values) }
                        };

                        eventForSending.EnvelopId = retryPolicyCtx.CorrelationId;

                        _logger.LogInformation(
                            "Sending event for operation: {OperationKey}, correlationId: {CorrelationId}",
                            eventForSending.Payload.Key,
                            retryPolicyCtx.CorrelationId);

                        var result = await base.SendAsync(
                            eventForSending,
                            streamName ?? "",
                            eventType ?? "",
                            token);

                        _logger.LogInformation(
                            "Event sent successfully for operation: {OperationKey}, correlationId: {CorrelationId}",
                            eventForSending.Payload.Key,
                            retryPolicyCtx.CorrelationId);

                        return result;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to send event for operation: {OperationKey}, correlationId: {CorrelationId}",
                            eventForSending.Payload.Key,
                            retryPolicyCtx.CorrelationId);

                        await SendToDeadLetterQueueAsync(eventForSending, ex);

                        // Re-throw the exception to trigger retry
                        throw;
                    }
                },
                context);
        }

        public override IAsyncEnumerable<PaymentOperationEvent> ReadAsync(
            string streamName,
            int maxEvents = int.MaxValue,
            CancellationToken token = default)
        {
            return base.ReadAsync(PaymentOperationNamesGenerator.GenerateForAccountMonthStream(streamName), maxEvents, token);
        }

        protected override Task OnEventAppearedAsync(PaymentOperationEvent eventData)
        {
            _paymentEventChannel.Writer.TryWrite(eventData);
            return Task.CompletedTask;
        }

        private async Task ProcessEventBatchAsync()
        {
            while (await _paymentEventChannel.Reader.WaitToReadAsync())
            {
                while (_paymentEventChannel.Reader.TryRead(out var evt))
                {
                    _latestEventsPerAccount[evt.Payload.GetMonthPeriodIdentifier()] = evt;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_eventStoreDbOptions.EventBatchingDelayInMs));

                // Process the latest events for all accounts
                foreach (var (monthFinancialPeriodKey, latestEvent) in _latestEventsPerAccount)
                {
                    _ = Task.Run(() => HandlePaymentOperationEventAsync(latestEvent.Payload));
                    _latestEventsPerAccount.Remove(monthFinancialPeriodKey, out _);
                }
            }
        }

        private async Task HandlePaymentOperationEventAsync(FinancialTransaction transaction)
        {
            var paymentAccountId = transaction.PaymentAccountId;
            var paymentPeriodAggregationId = transaction.GetMonthPeriodIdentifier();

            var events = await BenchmarkService.WithBenchmarkAsync(
                async () => await ReadAsync(transaction.GetMonthPeriodIdentifier()).ToListAsync(),
                $"Fetching events for '{paymentPeriodAggregationId}'",
                _logger,
                new { paymentPeriodEdIdentifier = paymentPeriodAggregationId });

            var processedAt = DateTime.UtcNow;

            foreach (var operationEvent in events)
            {
                operationEvent.ProcessedAt = processedAt;
            }

            _ = Task.Run(() =>
                    BenchmarkService.WithBenchmarkAsync(
                        async () =>
                        {
                            await using var scope = _serviceScopeFactory.CreateAsyncScope();
                            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                            await sender.Send(new SyncOperationsHistoryCommand(paymentAccountId, events));
                        },
                        $"Sending SyncOperationsHistoryCommand for '{events.Count}' events",
                        _logger,
                        new { paymentPeriodEdIdentifier = paymentPeriodAggregationId })
                    );
        }
    }
}
