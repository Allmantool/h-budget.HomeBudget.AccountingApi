using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediatR;
using Polly;
using Polly.Retry;

using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;
using HomeBudget.Core;
using HomeBudget.Components.Operations.Commands.Models;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsEventStoreClient(
        ILogger<PaymentOperationsEventStoreClient> logger,
        EventStoreClient client,
        IOptions<EventStoreDbOptions> options,
        ISender sender)
        : BaseEventStoreClient<PaymentOperationEvent>(client, options.Value)
    {
        private readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<RpcException>(ex => ex.StatusCode == StatusCode.DeadlineExceeded)
            .WaitAndRetryAsync(
                retryCount: options.Value.RetryAttempts,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryAttempt, context) =>
                {
                    var eventName = context[nameof(PaymentOperationEvent.EventType)] as string;

                    logger.LogWarning(
                        "Retry attempt '{RetryAttempt}' for event '{EventName}' failed. Waiting '{RetryDelay}' before next attempt. Exception: {Exception}",
                        retryAttempt,
                        eventName,
                        timeSpan,
                        exception.Message);
                });

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

        protected override async Task OnEventAppeared(PaymentOperationEvent eventData)
        {
            var paymentAccountId = eventData.Payload.PaymentAccountId;

            var eventsForAccount = await BenchmarkService.WithBenchmarkAsync(
                async () => await ReadAsync(paymentAccountId.ToString()).ToListAsync(),
                $"Fetching events for account '{paymentAccountId}'",
                logger,
                new { PaymentAccountId = paymentAccountId });

            logger.LogInformation("Sync history for '{EventsAmount}' events for account '{PaymentAccountId}'", eventsForAccount.Count, paymentAccountId);

            await BenchmarkService.WithBenchmarkAsync(
                async () => await sender.Send(new SyncOperationsHistoryCommand(paymentAccountId, eventsForAccount)),
                "Sending SyncOperationsHistoryCommand",
                logger,
                new { PaymentAccountId = paymentAccountId });
        }
    }
}
