using System;
using System.Text.Json;

using Confluent.Kafka;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations
{
    internal static class PaymentEventToMessageConverter
    {
        public static Result<Message<string, string>> Convert(PaymentOperationEvent paymentEvent)
        {
            var eventPayload = paymentEvent?.Payload;

            if (eventPayload == null)
            {
                return Result<Message<string, string>>.Failure($"'{nameof(PaymentOperationEvent)}' can not be null");
            }

            var message = new Message<string, string>
            {
                Key = eventPayload.GetIdentifier(),
                Value = JsonSerializer.Serialize(paymentEvent),
                Timestamp = new Timestamp(DateTime.UtcNow),
            };

            return Result<Message<string, string>>.Succeeded(message);
        }
    }
}
