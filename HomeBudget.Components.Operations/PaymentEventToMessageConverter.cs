using System;
using System.Text.Json;

using Confluent.Kafka;

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
                Value = JsonSerializer.Serialize(paymentEvent),
                Timestamp = new Timestamp(DateTime.Now),
            };
        }
    }
}
