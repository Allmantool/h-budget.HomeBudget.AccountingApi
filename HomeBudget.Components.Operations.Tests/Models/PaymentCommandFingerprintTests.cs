using System;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Tests.Models
{
    [TestFixture]
    public class PaymentCommandFingerprintTests
    {
        [Test]
        public void Create_WhenEquivalentGuidFormattingIsUsed_ThenProducesTheSameFingerprint()
        {
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var contractorId = Guid.NewGuid();
            var first = new PaymentOperationPayload
            {
                Amount = 1.20m,
                CategoryId = categoryId.ToString("D").ToUpperInvariant(),
                ContractorId = contractorId.ToString("D"),
                OperationDate = new DateOnly(2026, 9, 2),
                ScopeOperationId = 4,
                Comment = "coffee"
            };
            var second = first with
            {
                CategoryId = categoryId.ToString("N"),
                ContractorId = contractorId.ToString("B")
            };

            var firstFingerprint = PaymentCommandFingerprint.Create(PaymentCommandTypes.Create, accountId, null, first);
            var secondFingerprint = PaymentCommandFingerprint.Create(PaymentCommandTypes.Create, accountId, null, second);

            secondFingerprint.Should().Be(firstFingerprint);
        }

        [Test]
        public void Create_WhenFinancialPayloadChanges_ThenProducesADifferentFingerprint()
        {
            var accountId = Guid.NewGuid();
            var payload = new PaymentOperationPayload
            {
                Amount = 1.20m,
                OperationDate = new DateOnly(2026, 9, 2),
                Comment = "coffee"
            };

            var firstFingerprint = PaymentCommandFingerprint.Create(PaymentCommandTypes.Create, accountId, null, payload);
            var secondFingerprint = PaymentCommandFingerprint.Create(
                PaymentCommandTypes.Create,
                accountId,
                null,
                payload with { Amount = 1.21m });

            secondFingerprint.Should().NotBe(firstFingerprint);
        }

        [Test]
        public void Create_WhenCommentContainsCanonicalDelimiter_ThenDoesNotCollideWithAnotherFieldLayout()
        {
            var accountId = Guid.NewGuid();
            var withDelimiter = new PaymentOperationPayload
            {
                Amount = 1m,
                Comment = "a|b"
            };
            var withoutDelimiter = new PaymentOperationPayload
            {
                Amount = 1m,
                Comment = "a"
            };

            var withDelimiterFingerprint = PaymentCommandFingerprint.Create(
                PaymentCommandTypes.Create,
                accountId,
                null,
                withDelimiter);
            var withoutDelimiterFingerprint = PaymentCommandFingerprint.Create(
                PaymentCommandTypes.Create,
                accountId,
                null,
                withoutDelimiter);

            withDelimiterFingerprint.Should().NotBe(withoutDelimiterFingerprint);
        }
    }
}
