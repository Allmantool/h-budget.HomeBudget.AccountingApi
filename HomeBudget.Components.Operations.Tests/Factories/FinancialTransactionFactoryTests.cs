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
            var result = _sut.CreatePayment(
                Guid.Empty,
                10m,
                "some comment",
                Guid.Empty.ToString(),
                Guid.Empty.ToString(),
                new DateOnly(2024, 05, 13));

            result.Payload.TransactionType.Should().Be(TransactionTypes.Payment);
        }

        [Test]
        public void CreateTransfer_ThenTransferHasBeenCreated()
        {
            var result = _sut.CreateTransfer(Guid.Empty, 20, new DateOnly(2024, 05, 13));

            result.Payload.TransactionType.Should().Be(TransactionTypes.Transfer);
        }
    }
}
