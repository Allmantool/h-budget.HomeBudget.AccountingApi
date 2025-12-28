using System;
using System.Text;
using System.Text.Json;

using Confluent.Kafka;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Components.Operations.Commands.Handlers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations
{
    internal static class PaymentEventToMessageConverter
    {
        public static Result<Message<string, string>> Convert(PaymentOperationEvent paymentEvent, DateTime? createdAt = null)
        {
            var eventPayload = paymentEvent?.Payload;

            if (eventPayload == null)
            {
                return Result<Message<string, string>>.Failure($"'{nameof(PaymentOperationEvent)}' can not be null");
            }

            var enquiredAt = createdAt ?? DateTime.UtcNow;
            var messageId = eventPayload.GetPaymentAccountIdentifier();
            var messagePayload = JsonSerializer.Serialize(paymentEvent);

            var headers = new Headers
            {
                new Header(KafkaMessageHeaders.CorrelationId, Encoding.UTF8.GetBytes(paymentEvent.Metadata[EventMetadataKeys.CorrelationId])),
                new Header(KafkaMessageHeaders.Type, Encoding.UTF8.GetBytes(nameof(PaymentOperationEvent))),
                new Header(KafkaMessageHeaders.Version, Encoding.UTF8.GetBytes("2.0")),
                new Header(KafkaMessageHeaders.Source, Encoding.UTF8.GetBytes(nameof(BasePaymentCommandHandler))),
                new Header(KafkaMessageHeaders.EnvelopId, Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                new Header(KafkaMessageHeaders.OccuredOn, Encoding.UTF8.GetBytes(enquiredAt.ToString("O"))),
            };

            var message = new Message<string, string>
            {
                Key = messageId,
                Value = messagePayload,
                Timestamp = new Timestamp(enquiredAt),
                Headers = headers
            };

            return Result<Message<string, string>>.Succeeded(message);
        }
    }
}
