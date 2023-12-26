using Confluent.Kafka;

using Microsoft.Extensions.Logging;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Handlers
{
    internal class PaymentOperationsDeliveryHandler(ILogger<PaymentOperationsDeliveryHandler> logger) : IPaymentOperationsDeliveryHandler
    {
        public void Handle(DeliveryReport<string, string> deliveryReport)
        {
            if (deliveryReport.Status == PersistenceStatus.Persisted)
            {
                logger.LogInformation($"'{deliveryReport.Key}' -- {typeof(PaymentOperationEvent)} has been stream successfully");
            }

            if (deliveryReport.Status == PersistenceStatus.NotPersisted)
            {
                logger.LogError($"'{deliveryReport.Key}' -- {typeof(PaymentOperationEvent)} streaming failed");
            }
        }
    }
}
