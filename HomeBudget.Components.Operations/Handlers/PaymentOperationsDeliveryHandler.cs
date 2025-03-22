using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Handlers
{
    internal class PaymentOperationsDeliveryHandler(
        ILogger<PaymentOperationsDeliveryHandler> logger,
        IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient)
        : IPaymentOperationsDeliveryHandler
    {
        public async Task HandleAsync(PaymentOperationEvent paymentEvent, CancellationToken cancellationToken)
        {
            try
            {
                var eventPayload = paymentEvent.Payload;
                var paymentOperationId = eventPayload.Key;

                var eventTypeTitle = $"{paymentEvent.EventType}_{paymentOperationId}";
                var accountPerMonthIdentifier = eventPayload.GetMonthPeriodIdentifier();
                var streamName = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(accountPerMonthIdentifier);

                await eventStoreDbClient.SendAsync(
                    paymentEvent,
                    streamName,
                    eventTypeTitle,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Handler} with error: {ExceptionMessage}", nameof(PaymentOperationsDeliveryHandler), ex.Message);
            }
        }
    }
}
