using System;
using System.Text;
using System.Text.Json;

using Confluent.Kafka;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Components.Operations.Commands.Handlers;
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
                Headers =
                {
                    new Header(KafkaMessageHeaders.Type, Encoding.UTF8.GetBytes(nameof(PaymentOperationEvent))),
                    new Header(KafkaMessageHeaders.Version, Encoding.UTF8.GetBytes("1.0")),
                    new Header(KafkaMessageHeaders.Source, Encoding.UTF8.GetBytes(nameof(BasePaymentCommandHandler))),
                    new Header(KafkaMessageHeaders.EnvelopId, Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(KafkaMessageHeaders.OccuredOn, Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O"))),
                }
            };

            return Result<Message<string, string>>.Succeeded(message);
        }
    }
}
