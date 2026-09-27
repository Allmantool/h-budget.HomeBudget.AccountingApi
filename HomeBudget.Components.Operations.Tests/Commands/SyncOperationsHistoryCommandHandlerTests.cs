using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Components.Accounts.Services.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Handlers;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Tests.Commands
{
    [TestFixture]
    public sealed class SyncOperationsHistoryCommandHandlerTests
    {
        [Test]
        public async Task SuccessfulProjection_MarksCommandProjectedAfterHeadAndFencedBalance()
        {
            var order = new List<string>();
            var dependencies = BuildDependencies(order, balanceSucceeded: true);
            var handler = dependencies.CreateHandler();

            var result = await handler.Handle(dependencies.Command, CancellationToken.None);

            result.IsSucceeded.Should().BeTrue();
            order.Should().ContainInOrder("head", "balance-snapshot", "fenced-balance", "ack", "audit");
            order[^1].Should().Be("audit");
        }

        [Test]
        public async Task FencedBalanceFailure_DoesNotMarkCommandProjected()
        {
            var order = new List<string>();
            var dependencies = BuildDependencies(order, balanceSucceeded: false);
            var handler = dependencies.CreateHandler();

            var execute = async () => await handler.Handle(dependencies.Command, CancellationToken.None);

            await execute.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Fenced balance publication failed*");
            order.Should().ContainInOrder("head", "balance-snapshot", "fenced-balance");
            order.Should().NotContain("ack");
            order[^1].Should().Be("audit");
        }

        private static HandlerDependencies BuildDependencies(List<string> order, bool balanceSucceeded)
        {
            var accountId = Guid.NewGuid();
            var paymentEvent = new PaymentOperationEvent
            {
                Payload = new FinancialTransaction
                {
                    Key = Guid.NewGuid(),
                    PaymentAccountId = accountId,
                    OperationDay = new DateOnly(2026, 1, 5),
                    Amount = 10m
                }
            };
            paymentEvent.Metadata[EventMetadataKeys.CommandId] = "command-1";
            var historyService = new Mock<IPaymentOperationsHistoryService>();
            historyService.Setup(service => service.SyncHistoryAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<ProjectionCheckpoint>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Guid?>()))
                .Callback(() => order.Add("head"))
                .ReturnsAsync(Result<decimal>.Succeeded(10m));
            var historyClient = new Mock<IPaymentsHistoryDocumentsClient>();
            historyClient.Setup(client => client.CompleteProjectionRunAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Callback(() => order.Add("audit"))
                .Returns(Task.CompletedTask);
            historyClient.Setup(client => client.CreateAccountBalanceSnapshotAsync(
                    accountId,
                    It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("balance-snapshot"))
                .ReturnsAsync(new AccountProjectionBalanceSnapshot(7, 10m));
            var accountService = new Mock<IPaymentAccountService>();
            accountService.Setup(service => service.GetInitialBalanceAsync(accountId.ToString()))
                .ReturnsAsync(100m);
            var sender = new Mock<ISender>();
            sender.Setup(current => current.Send(
                    It.Is<UpdatePaymentAccountBalanceCommand>(command => command.ProjectionFence == 7),
                    It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("fenced-balance"))
                .ReturnsAsync(balanceSucceeded
                    ? Result<Guid>.Succeeded(accountId)
                    : Result<Guid>.Failure("simulated balance write failure"));
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox.Setup(service => service.MarkProjectedAsync("command-1", It.IsAny<DateTime>()))
                .Callback(() => order.Add("ack"))
                .Returns(Task.CompletedTask);
            return new HandlerDependencies(
                new SyncOperationsHistoryCommand(accountId, [paymentEvent]),
                sender,
                accountService,
                historyClient,
                historyService,
                outbox);
        }

        private sealed record HandlerDependencies(
            SyncOperationsHistoryCommand Command,
            Mock<ISender> Sender,
            Mock<IPaymentAccountService> AccountService,
            Mock<IPaymentsHistoryDocumentsClient> HistoryClient,
            Mock<IPaymentOperationsHistoryService> HistoryService,
            Mock<IOutboxPaymentStatusService> Outbox)
        {
            public SyncOperationsHistoryCommandHandler CreateHandler()
            {
                return new SyncOperationsHistoryCommandHandler(
                    Sender.Object,
                    NullLogger<SyncOperationsHistoryCommandHandler>.Instance,
                    AccountService.Object,
                    HistoryClient.Object,
                    HistoryService.Object,
                    Outbox.Object);
            }
        }
    }
}
