using System;
using System.Text.Json;

using Confluent.Kafka;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations
{
    internal static class PaymentEventToMessageConverter
    {
        public static Result<Message<string, string>> Convert(PaymentOperationEvent paymentEvent)
        {
            var eventPayload = paymentEvent?.Payload;

            if (eventPayload == null)
            {
                return new Result<Message<string, string>>(
                    isSucceeded: false,
                    message: $"'{nameof(PaymentOperationEvent)}' can not be null");
            }

            var message = new Message<string, string>
            {
                Key = $"{eventPayload.PaymentAccountId}-{eventPayload.Key}",
                Value = JsonSerializer.Serialize(paymentEvent),
                Timestamp = new Timestamp(DateTime.Now),
            };

            return new Result<Message<string, string>>(message);
        }
    }
}
