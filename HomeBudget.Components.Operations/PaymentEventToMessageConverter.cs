using Confluent.Kafka;
using Newtonsoft.Json;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations
{
    internal static class PaymentEventToMessageConverter
    {
        public static Message<string, string> Convert(PaymentOperationEvent paymentEvent)
        {
            var eventPayload = paymentEvent.Payload;

            return new Message<string, string>
            {
                Key = $"{eventPayload.PaymentAccountId}-{eventPayload.Key}",
                Value = $"{paymentEvent.EventType}-{JsonConvert.SerializeObject(eventPayload)}"
            };
        }
    }
}
