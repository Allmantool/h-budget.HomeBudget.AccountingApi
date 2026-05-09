using System;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Components.Operations.Factories;

namespace HomeBudget.Components.Operations.Tests.Factories
{
    [TestFixture]
    public class FinancialTransactionFactoryTests
    {
        private readonly FinancialTransactionFactory _sut = new();

        [Test]
        public void CreatePayment_ThenPaymentHasBeenCreated()
        {
            var categoryId = Guid.NewGuid();
            var contractorId = Guid.NewGuid();

            var result = _sut.CreatePayment(
                Guid.Empty,
                120,
                10m,
                "some comment",
                categoryId.ToString(),
                contractorId.ToString(),
                new DateOnly(2024, 05, 13));

            Assert.Multiple(() =>
            {
                result.Payload.TransactionType.Should().Be(TransactionTypes.Payment);
                result.Payload.CategoryId.Should().Be(categoryId);
                result.Payload.ContractorId.Should().Be(contractorId);
            });
        }

        [Test]
        public void CreatePayment_WhenOptionalReferencesAreMissing_ThenExplicitEmptyReferencesAreUsed()
        {
            var result = _sut.CreatePayment(
                Guid.Empty,
                120,
                10m,
                "some comment",
                null,
                string.Empty,
                new DateOnly(2024, 05, 13));

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeTrue();
                result.Payload.CategoryId.Should().Be(Guid.Empty);
                result.Payload.ContractorId.Should().Be(Guid.Empty);
            });
        }

        [Test]
        public void CreatePayment_WhenCategoryReferenceIsInvalid_ThenFailureIsReturned()
        {
            var result = _sut.CreatePayment(
                Guid.Empty,
                120,
                10m,
                "some comment",
                "invalid-category",
                Guid.NewGuid().ToString(),
                new DateOnly(2024, 05, 13));

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeFalse();
                result.StatusMessage.Should().Contain("categoryId");
                result.Payload.Should().BeNull();
            });
        }

        [Test]
        public void CreatePayment_WhenContractorReferenceIsInvalid_ThenFailureIsReturned()
        {
            var result = _sut.CreatePayment(
                Guid.Empty,
                120,
                10m,
                "some comment",
                Guid.NewGuid().ToString(),
                "invalid-contractor",
                new DateOnly(2024, 05, 13));

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeFalse();
                result.StatusMessage.Should().Contain("contractorId");
                result.Payload.Should().BeNull();
            });
        }

        [Test]
        public void CreateTransfer_ThenTransferHasBeenCreated()
        {
            var result = _sut.CreateTransfer(Guid.Empty, 20, new DateOnly(2024, 05, 13));

            result.Payload.TransactionType.Should().Be(TransactionTypes.Transfer);
        }
    }
}
