using System;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Tests.Models
{
    [TestFixture]
    public sealed class TransferCommandContractTests
    {
        [Test]
        public void Fingerprint_PreservesExactCrossCurrencyAmounts()
        {
            var payload = Payload();
            var changedRecipient = payload with { RecipientAmount = 6030.0001m };

            TransferCommandFingerprint.Create(payload).Should().Be(TransferCommandFingerprint.Create(payload));
            TransferCommandFingerprint.Create(payload).Should().NotBe(TransferCommandFingerprint.Create(changedRecipient));
        }

        [TestCase(7, 7, PaymentCommandStatus.Projected)]
        [TestCase(7, 6, PaymentCommandStatus.Persisted)]
        [TestCase(7, 1, PaymentCommandStatus.Published)]
        [TestCase(7, 0, PaymentCommandStatus.Accepted)]
        [TestCase(5, 7, PaymentCommandStatus.Failed)]
        public void Lifecycle_IsConservativeAcrossBothSides(
            byte senderStatus,
            byte recipientStatus,
            PaymentCommandStatus expected)
        {
            var record = new TransferCommandRecord
            {
                SenderStatus = senderStatus,
                RecipientStatus = recipientStatus
            };

            TransferCommandLifecycle.Evaluate(record).Should().Be(expected);
        }

        private static CrossAccountsTransferPayload Payload() => new()
        {
            Sender = Guid.Parse("975a222c-c606-4608-9ef9-7d42a43bba90"),
            Recipient = Guid.Parse("0dd44772-7f1a-455d-a5e7-513b0eec5b52"),
            SenderAmount = 1800m,
            RecipientAmount = 6030m,
            SenderCurrency = "USD",
            RecipientCurrency = "BYN",
            OperationAt = new DateOnly(2025, 1, 24),
            SourceReference = "FamilyPro12:hash:TRANSFER:23460:23461"
        };
    }
}
