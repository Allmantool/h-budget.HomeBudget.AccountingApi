﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediatR;
using Polly;
using Polly.Retry;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;
using HomeBudget.Core;
using HomeBudget.Components.Operations.Commands.Models;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsEventStoreClient : BaseEventStoreClient<PaymentOperationEvent>
    {
        private readonly Channel<PaymentOperationEvent> _paymentEventChannel = Channel.CreateUnbounded<PaymentOperationEvent>();
        private readonly ConcurrentDictionary<Guid, PaymentOperationEvent> _latestEventsPerAccount = new();

        private readonly AsyncRetryPolicy _retryPolicy;

        private readonly ILogger<PaymentOperationsEventStoreClient> _logger;
        private readonly ISender _sender;
        private readonly EventStoreDbOptions _eventStoreDbOptions;

        public PaymentOperationsEventStoreClient(
            ILogger<PaymentOperationsEventStoreClient> logger,
            EventStoreClient client,
            IOptions<EventStoreDbOptions> options,
            ISender sender) : base(client, options.Value)
        {
            _eventStoreDbOptions = options.Value;
            _logger = logger;
            _sender = sender;
            _retryPolicy = Policy
                .Handle<RpcException>(ex => ex.StatusCode == StatusCode.DeadlineExceeded)
                .WaitAndRetryAsync(
                    retryCount: _eventStoreDbOptions.RetryAttempts,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryAttempt, context) =>
                    {
                        var eventName = context[nameof(PaymentOperationEvent.EventType)] as string;

                        logger.LogWarning(
                            "Retry attempt '{RetryAttempt}' for event '{EventName}' failed. Waiting '{RetryDelay}' before next attempt. " +
                            "Exception: {Exception}",
                            retryAttempt,
                            eventName,
                            timeSpan,
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
            var context = new Context { [nameof(PaymentOperationEvent.EventType)] = eventType };

            return await _retryPolicy.ExecuteAsync(
                async (_) => await base.SendAsync(
                    eventForSending,
                    streamName ?? "",
                    eventType ?? "",
                    token),
                context);
        }

        public override IAsyncEnumerable<PaymentOperationEvent> ReadAsync(
            string streamName,
            int maxEvents = int.MaxValue,
            CancellationToken token = default)
        {
            return base.ReadAsync(PaymentOperationNamesGenerator.GetEventSteamName(streamName), maxEvents, token);
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
                    _latestEventsPerAccount[evt.Payload.PaymentAccountId] = evt;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_eventStoreDbOptions.EventBatchingDelayInMs));

                // Process the latest events for all accounts
                foreach (var (paymentAccountId, latestEvent) in _latestEventsPerAccount.ToArray())
                {
                    await HandlePaymentOperationEventAsync(latestEvent.Payload);
                    _latestEventsPerAccount.Remove(paymentAccountId, out _);
                }
            }
        }

        private async Task HandlePaymentOperationEventAsync(FinancialTransaction transaction)
        {
            var paymentAccountId = transaction.PaymentAccountId;

            var eventsForAccount = await BenchmarkService.WithBenchmarkAsync(
                async () => await ReadAsync(paymentAccountId.ToString()).ToListAsync(),
                $"Fetching events for account '{paymentAccountId}'",
                _logger,
                new { PaymentAccountId = paymentAccountId });

            await BenchmarkService.WithBenchmarkAsync(
                async () => await _sender.Send(new SyncOperationsHistoryCommand(paymentAccountId, eventsForAccount)),
                $"Sending SyncOperationsHistoryCommand for '{eventsForAccount.Count}' events",
                _logger,
                new { PaymentAccountId = paymentAccountId });
        }
    }
}
