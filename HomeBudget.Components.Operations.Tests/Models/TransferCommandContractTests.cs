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

        [Test]
        public void Xfer003SenderChildDelayedDoesNotReportTerminalCompletion()
        {
            var record = TransferRecord() with
            {
                SenderStatus = 6,
                RecipientStatus = 7
            };

            TransferCommandLifecycle.Evaluate(record).Should().Be(PaymentCommandStatus.Persisted);
            record.SenderAmount.Should().Be(1800m);
            record.RecipientAmount.Should().Be(6030m);
            record.SenderCurrency.Should().Be("USD");
            record.RecipientCurrency.Should().Be("BYN");
        }

        [Test]
        public void Xfer004RecipientChildDelayedDoesNotReportTerminalCompletion()
        {
            var record = TransferRecord() with
            {
                SenderStatus = 7,
                RecipientStatus = 6
            };

            TransferCommandLifecycle.Evaluate(record).Should().Be(PaymentCommandStatus.Persisted);
            record.SenderOperationId.Should().NotBe(record.RecipientOperationId);
            record.SenderAccountId.Should().NotBe(record.RecipientAccountId);
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

        private static TransferCommandRecord TransferRecord() => new()
        {
            TransferId = Guid.Parse("783467d4-7101-40ea-9ad7-392f9ec12619"),
            SenderAccountId = Guid.Parse("975a222c-c606-4608-9ef9-7d42a43bba90"),
            RecipientAccountId = Guid.Parse("0dd44772-7f1a-455d-a5e7-513b0eec5b52"),
            SenderOperationId = Guid.Parse("78c62d04-0c06-4d28-b4b4-220420f2c4f7"),
            RecipientOperationId = Guid.Parse("f060c3c6-2248-4acf-979a-bf4262382739"),
            SenderAmount = 1800m,
            RecipientAmount = 6030m,
            SenderCurrency = "USD",
            RecipientCurrency = "BYN",
            OperationDate = new DateOnly(2025, 1, 24),
            SourceReference = "FamilyPro12:hash:TRANSFER:23460:23461"
        };
    }
}
