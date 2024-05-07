using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Handlers
{
    internal class PaymentOperationsDeliveryHandler(
        IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient,
        ILogger<PaymentOperationsDeliveryHandler> logger)
        : IPaymentOperationsDeliveryHandler
    {
        public async Task HandleAsync(PaymentOperationEvent paymentEvent, CancellationToken cancellationToken)
        {
            await eventStoreDbClient.SendAsync(
                paymentEvent,
                token: cancellationToken);

            logger.LogInformation("'{eventIdentifier}' has been streamed successfully", paymentEvent.Payload.GetIdentifier());
        }
    }
}
