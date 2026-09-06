using System;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Validators;

namespace HomeBudget.Components.Operations.Tests.Validators
{
    [TestFixture]
    public class PaymentAmountCommandValidatorTests
    {
        [TestCase(0)]
        [TestCase(-10)]
        public void AddPaymentOperationCommand_WhenAmountIsNotPositive_ThenValidationFails(decimal amount)
        {
            var failures = new AddPaymentOperationCommandValidator().Validate(
                new AddPaymentOperationCommand(CreatePayment(amount)));

            failures.Should().Contain("Amount must be greater than zero");
        }

        [TestCase(0)]
        [TestCase(-10)]
        public void UpdatePaymentOperationCommand_WhenAmountIsNotPositive_ThenValidationFails(decimal amount)
        {
            var failures = new UpdatePaymentOperationCommandValidator().Validate(
                new UpdatePaymentOperationCommand(CreatePayment(amount)));

            failures.Should().Contain("Amount must be greater than zero");
        }

        [Test]
        public void ApplyTransferCommand_WhenAmountsAreSigned_ThenValidationSucceeds()
        {
            var sender = CreatePayment(-10m);
            sender.TransactionType = TransactionTypes.Transfer;
            var recipient = CreatePayment(10m);
            recipient.TransactionType = TransactionTypes.Transfer;
            var transfer = new CrossAccountsTransferOperation
            {
                PaymentOperations = [sender, recipient]
            };

            var failures = new ApplyTransferCommandValidator().Validate(new ApplyTransferCommand(transfer));

            failures.Should().BeEmpty();
        }

        private static FinancialTransaction CreatePayment(decimal amount)
        {
            return new FinancialTransaction
            {
                Amount = amount,
                Key = Guid.NewGuid(),
                OperationDay = new DateOnly(2026, 9, 5),
                PaymentAccountId = Guid.NewGuid(),
                TransactionType = TransactionTypes.Payment
            };
        }
    }
}
