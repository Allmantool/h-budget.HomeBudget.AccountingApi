using System;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;

namespace HomeBudget.Components.Operations.Tests.Clients
{
    [TestFixture]
    public class PaymentOperationEventIdentityTests
    {
        [Test]
        public void GetDeterministicEventId_WhenSameMessageAndEventType_ThenReturnsSameId()
        {
            var firstEvent = CreatePaymentEvent();
            var secondEvent = CreatePaymentEvent(firstEvent.Payload.Key);
            firstEvent.Metadata[EventMetadataKeys.MessageId] = "message-1";
            secondEvent.Metadata[EventMetadataKeys.MessageId] = "message-1";

            var firstId = PaymentOperationEventIdentity.GetDeterministicEventId(firstEvent, "Added_operation");
            var secondId = PaymentOperationEventIdentity.GetDeterministicEventId(secondEvent, "Added_operation");

            secondId.Should().Be(firstId);
        }

        [Test]
        public void GetDeterministicEventId_WhenEventTypeChanges_ThenReturnsDifferentId()
        {
            var paymentEvent = CreatePaymentEvent();
            paymentEvent.Metadata[EventMetadataKeys.MessageId] = "message-2";

            var firstId = PaymentOperationEventIdentity.GetDeterministicEventId(paymentEvent, "Added_operation");
            var secondId = PaymentOperationEventIdentity.GetDeterministicEventId(paymentEvent, "Removed_operation");

            secondId.Should().NotBe(firstId);
        }

        [Test]
        public void EnsureMetadata_WhenMessageMetadataMissing_ThenUsesOperationIdFallback()
        {
            var paymentEvent = CreatePaymentEvent();

            PaymentOperationEventIdentity.EnsureMetadata(paymentEvent);

            paymentEvent.Metadata[EventMetadataKeys.MessageId].Should().Be(paymentEvent.Payload.Key.ToString("N"));
            paymentEvent.Metadata[EventMetadataKeys.CommandId].Should().Be(paymentEvent.Payload.Key.ToString("N"));
            paymentEvent.Metadata[EventMetadataKeys.SourceSystem].Should().Be(PaymentOperationEventIdentity.DefaultSourceSystem);
        }

        private static PaymentOperationEvent CreatePaymentEvent(Guid? operationId = null)
        {
            return new PaymentOperationEvent
            {
                EventType = PaymentEventTypes.Added,
                Payload = new FinancialTransaction
                {
                    Key = operationId ?? Guid.NewGuid(),
                    PaymentAccountId = Guid.NewGuid(),
                    OperationDay = new DateOnly(2026, 05, 09),
                    Amount = 25m
                }
            };
        }
    }
}
