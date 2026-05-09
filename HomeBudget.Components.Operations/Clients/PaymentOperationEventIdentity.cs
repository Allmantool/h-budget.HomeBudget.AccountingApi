using System;
using System.Security.Cryptography;
using System.Text;

using EventStore.Client;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;

namespace HomeBudget.Components.Operations.Clients
{
    internal static class PaymentOperationEventIdentity
    {
        public const string DefaultSourceSystem = "home-budget-accounting-api";

        public static Uuid GetDeterministicEventId(PaymentOperationEvent paymentEvent, string eventType)
        {
            ArgumentNullException.ThrowIfNull(paymentEvent);

            var sourceId = GetStableSourceId(paymentEvent);
            var source = $"payment-operation-event|{sourceId}|{eventType ?? paymentEvent.EventType.ToString()}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant()));
            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, guidBytes.Length);

            return Uuid.FromGuid(new Guid(guidBytes));
        }

        public static void EnsureMetadata(PaymentOperationEvent paymentEvent)
        {
            ArgumentNullException.ThrowIfNull(paymentEvent);

            var sourceId = GetStableSourceId(paymentEvent);
            SetIfMissing(paymentEvent, EventMetadataKeys.MessageId, sourceId);
            SetIfMissing(paymentEvent, EventMetadataKeys.CommandId, sourceId);
            SetIfMissing(paymentEvent, EventMetadataKeys.CorrelationId, sourceId);
            SetIfMissing(paymentEvent, EventMetadataKeys.CausationId, string.Empty);
            SetIfMissing(paymentEvent, EventMetadataKeys.SourceSystem, DefaultSourceSystem);
        }

        private static string GetStableSourceId(PaymentOperationEvent paymentEvent)
        {
            if (paymentEvent.Metadata.TryGetValue(EventMetadataKeys.MessageId, out var messageId) &&
                !string.IsNullOrWhiteSpace(messageId))
            {
                return messageId;
            }

            if (paymentEvent.Metadata.TryGetValue(EventMetadataKeys.CommandId, out var commandId) &&
                !string.IsNullOrWhiteSpace(commandId))
            {
                return commandId;
            }

            if (paymentEvent.Payload?.Key != Guid.Empty)
            {
                return paymentEvent.Payload.Key.ToString("N");
            }

            if (paymentEvent.EnvelopId != Guid.Empty)
            {
                return paymentEvent.EnvelopId.ToString("N");
            }

            throw new InvalidOperationException("Payment event must have message id, command id, envelope id, or operation id.");
        }

        private static void SetIfMissing(PaymentOperationEvent paymentEvent, string key, string value)
        {
            if (!paymentEvent.Metadata.TryGetValue(key, out var existing) || string.IsNullOrWhiteSpace(existing))
            {
                paymentEvent.Metadata[key] = value ?? string.Empty;
            }
        }
    }
}
