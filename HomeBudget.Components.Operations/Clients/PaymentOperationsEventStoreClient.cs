using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using MediatR;
using EventStore.Client;

using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentOperationsEventStoreClient(
        IServiceProvider serviceProvider,
        EventStoreClient client,
        ISender sender)
        : BaseEventStoreClient<PaymentOperationEvent>(client)
    {
        public override async Task<IWriteResult> SendAsync(
            PaymentOperationEvent eventForSending,
            string streamName = default,
            string eventType = default,
            CancellationToken token = default)
        {
            var paymentAccountId = eventForSending.Payload.PaymentAccountId;
            var paymentOperationId = eventForSending.Payload.Key;

            return await base.SendAsync(
                eventForSending,
                PaymentOperationNamesGenerator.GetEventSteamName(paymentAccountId.ToString()),
                $"{eventForSending.EventType}_{paymentOperationId}",
                token);
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
