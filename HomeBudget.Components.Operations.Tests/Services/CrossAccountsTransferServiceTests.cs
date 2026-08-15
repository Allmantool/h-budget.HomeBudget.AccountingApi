using System;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using MediatR;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public class CrossAccountsTransferServiceTests
    {
        [Test]
        public async Task ApplyAsync_WhenCustomConversionMultiplierIsProvided_ThenUsesItForBothTransferOperationsAsync()
        {
            var senderAccountId = Guid.NewGuid();
            var recipientAccountId = Guid.NewGuid();
            var senderOperation = new FinancialTransaction();
            var recipientOperation = new FinancialTransaction();
            var builder = new Mock<ICrossAccountsTransferBuilder>();
            var financialTransactionFactory = new Mock<IFinancialTransactionFactory>();
            var mediator = new Mock<ISender>();
            var paymentAccountDocumentClient = new Mock<IPaymentAccountDocumentClient>();

            financialTransactionFactory
                .Setup(factory => factory.CreateTransfer(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<DateOnly>()))
                .Returns((Guid accountId, decimal amount, DateOnly operationDate) =>
                {
                    var operation = accountId == senderAccountId ? senderOperation : recipientOperation;
                    operation.Amount = amount;
                    operation.OperationDay = operationDate;
                    operation.PaymentAccountId = accountId;

                    return Result<FinancialTransaction>.Succeeded(operation);
                });
            builder.Setup(builder => builder.WithSender(senderOperation)).Returns(builder.Object);
            builder.Setup(builder => builder.WithRecipient(recipientOperation)).Returns(builder.Object);
            builder
                .Setup(builder => builder.BuildAsync())
                .ReturnsAsync(Result<CrossAccountsTransferOperation>.Succeeded(new CrossAccountsTransferOperation()));
            mediator
                .Setup(sender => sender.Send(It.IsAny<IRequest<Result<Guid>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Succeeded(Guid.NewGuid()));
            paymentAccountDocumentClient
                .Setup(client => client.GetByIdAsync(senderAccountId.ToString()))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(CreateAccountDocument("USD")));
            paymentAccountDocumentClient
                .Setup(client => client.GetByIdAsync(recipientAccountId.ToString()))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(CreateAccountDocument("BYN")));

            var sut = new CrossAccountsTransferService(
                mediator.Object,
                Mock.Of<IPaymentsHistoryDocumentsClient>(),
                paymentAccountDocumentClient.Object,
                financialTransactionFactory.Object,
                builder.Object);

            var result = await sut.ApplyAsync(
                new CrossAccountsTransferPayload
                {
                    Sender = senderAccountId,
                    Recipient = recipientAccountId,
                    Amount = 15m,
                    Multiplier = 2.977m,
                    CustomConversionMultiplier = 3.1m,
                    OperationAt = new(2026, 8, 15)
                },
                CancellationToken.None);

            result.IsSucceeded.Should().BeTrue();
            senderOperation.Amount.Should().Be(-15m);
            recipientOperation.Amount.Should().Be(46.5m);
            senderOperation.ConversionMultiplier.Should().Be(3.1m);
            recipientOperation.ConversionMultiplier.Should().Be(3.1m);
        }

        [Test]
        public async Task ApplyAsync_WhenCustomMultiplierIsUsedForSameCurrencyAccounts_ThenRejectsTheTransferAsync()
        {
            var senderAccountId = Guid.NewGuid();
            var recipientAccountId = Guid.NewGuid();
            var paymentAccountDocumentClient = new Mock<IPaymentAccountDocumentClient>();
            paymentAccountDocumentClient
                .Setup(client => client.GetByIdAsync(senderAccountId.ToString()))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(CreateAccountDocument("USD")));
            paymentAccountDocumentClient
                .Setup(client => client.GetByIdAsync(recipientAccountId.ToString()))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(CreateAccountDocument("USD")));

            var sut = new CrossAccountsTransferService(
                Mock.Of<ISender>(),
                Mock.Of<IPaymentsHistoryDocumentsClient>(),
                paymentAccountDocumentClient.Object,
                Mock.Of<IFinancialTransactionFactory>(),
                Mock.Of<ICrossAccountsTransferBuilder>());

            var result = await sut.ApplyAsync(
                new CrossAccountsTransferPayload
                {
                    Sender = senderAccountId,
                    Recipient = recipientAccountId,
                    Amount = 15m,
                    Multiplier = 1m,
                    CustomConversionMultiplier = 3.1m,
                    OperationAt = new(2026, 8, 15)
                },
                CancellationToken.None);

            result.IsSucceeded.Should().BeFalse();
            result.StatusMessage.Should().Contain("same-currency");
        }

        private static PaymentAccountDocument CreateAccountDocument(string currency)
        {
            return new PaymentAccountDocument
            {
                Payload = new PaymentAccount
                {
                    Currency = currency
                }
            };
        }
    }
}

