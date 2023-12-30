using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Handlers
{
    internal class PaymentOperationsDeliveryHandler(
        IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient,
        ILogger<PaymentOperationsDeliveryHandler> logger)
        : IPaymentOperationsDeliveryHandler
    {
        public async Task HandleAsync(DeliveryResult<string, string> deliveryResult, CancellationToken cancellationToken)
        {
            if (deliveryResult.Status == PersistenceStatus.Persisted)
            {
                var paymentSavedEvent = JsonSerializer.Deserialize<PaymentOperationEvent>(deliveryResult.Value);

                await eventStoreDbClient.SendAsync(
                    paymentSavedEvent,
                    token: cancellationToken);

                logger.LogInformation($"'{deliveryResult.Key}' -- {typeof(PaymentOperationEvent)} has been stream successfully");
            }

            if (deliveryResult.Status == PersistenceStatus.NotPersisted)
            {
                logger.LogError($"'{deliveryResult.Key}' -- {typeof(PaymentOperationEvent)} streaming failed");
            }
        }
    }
}
