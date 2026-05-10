using System;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Notifications.Models;
using HomeBudget.Accounting.Notifications.Services;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Commands.Handlers;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Tests.Commands.Handlers
{
    [TestFixture]
    internal sealed class UpdatePaymentAccountBalanceCommandHandlerTests
    {
        [Test]
        public async Task Handle_WhenNotificationPublishingFails_ReturnsSuccessAfterBalanceUpdate()
        {
            var accountId = Guid.NewGuid();
            var documentClient = new Mock<IPaymentAccountDocumentClient>();
            var notificationPublisher = new Mock<INotificationPublisher>();
            ILogger<UpdatePaymentAccountBalanceCommandHandler> logger = NullLogger<UpdatePaymentAccountBalanceCommandHandler>.Instance;
            var updatedBalance = 42.25m;
            PaymentAccount capturedAccount = null;

            documentClient
                .Setup(client => client.GetByIdAsync(accountId.ToString()))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(new PaymentAccountDocument
                {
                    Id = ObjectId.GenerateNewId(),
                    Payload = new PaymentAccount
                    {
                        Key = accountId,
                        InitialBalance = 10m,
                        Balance = 10m
                    }
                }));

            documentClient
                .Setup(client => client.UpdateAsync(accountId.ToString(), It.IsAny<PaymentAccount>()))
                .Callback<string, PaymentAccount>((_, account) => capturedAccount = account)
                .ReturnsAsync(Result<Guid>.Succeeded(accountId));

            notificationPublisher
                .Setup(publisher => publisher.PublishAsync(It.IsAny<PaymentAccountNotification>()))
                .ThrowsAsync(new InvalidOperationException("notification endpoint unavailable"));

            var sut = new UpdatePaymentAccountBalanceCommandHandler(
                documentClient.Object,
                notificationPublisher.Object,
                logger);

            var result = await sut.Handle(
                new UpdatePaymentAccountBalanceCommand(accountId, updatedBalance),
                CancellationToken.None);

            result.IsSucceeded.Should().BeTrue();
            result.Payload.Should().Be(accountId);
            capturedAccount.Should().NotBeNull();
            capturedAccount.Balance.Should().Be(updatedBalance);
            notificationPublisher.Verify(
                publisher => publisher.PublishAsync(It.IsAny<PaymentAccountNotification>()),
                Times.Once);
        }
    }
}
