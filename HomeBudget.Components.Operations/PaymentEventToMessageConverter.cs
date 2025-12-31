using System;
using System.Text.Json;

using Confluent.Kafka;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Factories;
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

            var message = new Message<string, string>
            {
                Key = messageId,
                Value = messagePayload,
                Timestamp = new Timestamp(enquiredAt),
                Headers = BuildHeaders(paymentEvent, enquiredAt)
            };

            return Result<Message<string, string>>.Succeeded(message);
        }

        private static Headers BuildHeaders(
            PaymentOperationEvent paymentEvent,
            DateTime enquiredAt)
        {
            return HeaderFactory
                .With(
                    KafkaMessageHeaders.CorrelationId,
                    paymentEvent.Metadata.Get(EventMetadataKeys.CorrelationId))
                .With(KafkaMessageHeaders.Type, nameof(PaymentOperationEvent))
                .With(KafkaMessageHeaders.Version, "2.0")
                .With(KafkaMessageHeaders.Source, nameof(BasePaymentCommandHandler))
                .With(
                    KafkaMessageHeaders.EnvelopId,
                    paymentEvent.EnvelopId != Guid.Empty
                        ? paymentEvent.EnvelopId.ToString()
                        : Guid.NewGuid().ToString())
                .With(KafkaMessageHeaders.OccuredOn, enquiredAt.ToString("O"))
                .Build();
        }
    }
}
