using Confluent.Kafka;

namespace HomeBudget.Components.Operations.Handlers;

internal interface IPaymentOperationsDeliveryHandler
{
    void Handle(DeliveryReport<string, string> deliveryReport);
}