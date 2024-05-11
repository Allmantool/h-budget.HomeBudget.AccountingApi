using System;
using System.Linq;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Builders;

namespace HomeBudget.Components.Operations.Tests.Builders
{
    [TestFixture]
    public class CrossAccountsTransferBuilderTests
    {
        private readonly CrossAccountsTransferBuilder _sut = new();

        [Test]
        public void Build_WhenRecipientOrSenderHaveNotBeenProvided_ThenNotSuccessfully()
        {
           var result = _sut.Build();

           result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void Build_WhenRecipientAndSenderAreProvided_ThenSuccessfully()
        {
            var result = _sut
                .WithRecipient(new PaymentOperation
                {
                    PaymentAccountId = Guid.Parse("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"),
                })
                .WithSender(new PaymentOperation
                {
                    PaymentAccountId = Guid.Parse("54095569-8e60-4500-b166-7b761dbe3103"),
                })
                .Build();

            result.IsSucceeded.Should().BeTrue();
        }

        [Test]
        public void Build_WithExplicitTransferId_ThenExpectedTransferIdForAllOperations()
        {
            var testTransferId = Guid.Parse("1938c4bd-6105-4979-9751-e08e0380aab7");

            var result = _sut
                .WithRecipient(new PaymentOperation
                {
                    PaymentAccountId = Guid.Parse("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"),
                })
                .WithSender(new PaymentOperation
                {
                    PaymentAccountId = Guid.Parse("54095569-8e60-4500-b166-7b761dbe3103"),
                })
                .WithTransferId(testTransferId)
                .Build();

            result.Payload.PaymentOperations.All(op => op.Key.Equals(testTransferId)).Should().BeTrue();
        }

        [Test]
        public void Build_WhenStandardBuild_ThenAlightWithRules()
        {
            var result = _sut
                .WithRecipient(new PaymentOperation
                {
                    PaymentAccountId = Guid.Parse("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"),
                })
                .WithSender(new PaymentOperation
                {
                    PaymentAccountId = Guid.Parse("54095569-8e60-4500-b166-7b761dbe3103"),
                })
                .Build();

            var transfer = result.Payload;
            var operations = transfer.PaymentOperations;

            Assert.Multiple(() =>
            {
                operations.First().Key = operations.Last().Key;

                operations.First().Key.Should().Be(transfer.Key);
                operations.Last().Key.Should().Be(transfer.Key);

                operations.First().PaymentAccountId.Should().NotBe(operations.Last().PaymentAccountId);
            });
        }
    }
}
