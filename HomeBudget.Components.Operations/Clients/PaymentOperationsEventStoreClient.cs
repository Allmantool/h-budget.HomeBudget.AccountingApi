using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MediatR;
using Polly;
using Polly.Retry;

using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsEventStoreClient(
        ILogger<PaymentOperationsEventStoreClient> logger,
        IServiceProvider serviceProvider,
        EventStoreClient client,
        EventStoreDbOptions options,
        ISender sender)
        : BaseEventStoreClient<PaymentOperationEvent>(client, options)
    {
        private readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<RpcException>(ex => ex.StatusCode == StatusCode.DeadlineExceeded)
            .WaitAndRetryAsync(
                options.RetryAttempts,
                retryAttempt =>
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                    logger.LogWarning(
                        "Retry attempt '{RetryAttempt}' for event writing failed. Retrying in '{RetryDelay}'.",
                        retryAttempt,
                        delay);

                    return delay;
                });

        public override async Task<IWriteResult> SendAsync(
            PaymentOperationEvent eventForSending,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            var paymentAccountId = eventForSending.Payload.PaymentAccountId;
            var paymentOperationId = eventForSending.Payload.Key;

            return await _retryPolicy.ExecuteAsync(
                async () => await base.SendAsync(
                    eventForSending,
                    PaymentOperationNamesGenerator.GetEventSteamName(paymentAccountId.ToString()),
                    $"{eventForSending.EventType}_{paymentOperationId}",
                    token));
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

            var eventsForAccount = await ReadAsync(paymentAccountId.ToString()).ToListAsync();

            using var scope = serviceProvider.CreateScope();
            var paymentOperationsHistoryService = scope.ServiceProvider.GetRequiredService<IPaymentOperationsHistoryService>();

            var upToDateBalanceResult = await paymentOperationsHistoryService.SyncHistoryAsync(paymentAccountId, eventsForAccount);

            await sender.Send(
                new UpdatePaymentAccountBalanceCommand(
                    paymentAccountId,
                    upToDateBalanceResult.Payload));
        }
    }
}
