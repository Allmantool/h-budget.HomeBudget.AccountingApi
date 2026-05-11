using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Components.Accounts.Services;
using HomeBudget.Components.Categories.Clients;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Commands.Handlers;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Commands.Handlers
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [Order(IntegrationTestOrderIndex.SyncOperationsHistoryHighLoadTests)]
    public class SyncOperationsHistoryCommandHandlerTests : BaseIntegrationTests
    {
        private Mock<ISender> _sender;
        private Mock<ILogger<SyncOperationsHistoryCommandHandler>> _logger;

        private CancellationToken _ct;

        private SyncOperationsHistoryCommandHandler _sut;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();

            _ct = CancellationToken.None;

            _logger = new Mock<ILogger<SyncOperationsHistoryCommandHandler>>();
            _sender = new Mock<ISender>();
            _sender
                .Setup(x => x.Send(It.IsAny<UpdatePaymentAccountBalanceCommand>(), _ct))
                .ReturnsAsync(Result<Guid>.Succeeded(Guid.Empty));
        }

        [Test]
        public async Task HighLoad_ProcessUpTo50kEvents_ShouldCompleteWithinReason()
        {
            var dbOptions = new MongoDbOptions();
            dbOptions.LedgerDatabase = $"{nameof(SyncOperationsHistoryCommandHandlerTests)}-{nameof(dbOptions.LedgerDatabase)}";
            dbOptions.PaymentAccounts = $"{nameof(SyncOperationsHistoryCommandHandlerTests)}-{nameof(dbOptions.PaymentAccounts)}";
            dbOptions.PaymentsHistory = $"{nameof(SyncOperationsHistoryCommandHandlerTests)}-{nameof(dbOptions.PaymentsHistory)}";
            dbOptions.HandBooks = $"{nameof(SyncOperationsHistoryCommandHandlerTests)}-{nameof(dbOptions.HandBooks)}";
            dbOptions.BulkInsertChunkSize = 500;
            dbOptions.ConnectionString = TestContainers.MongoDbContainer.GetConnectionString();

            var mongoDbOptions = Options.Create(dbOptions);

            using var paymentAccountDocumentsClient = new PaymentAccountDocumentClient(mongoDbOptions);
            using var paymentsHistoryDocumentsClient = new PaymentsHistoryDocumentsClient(mongoDbOptions);
            using var categoryDocumentsClient = new CategoryDocumentsClient(mongoDbOptions);

            var paymentAccountService = new PaymentAccountService(paymentAccountDocumentsClient);
            var paymentOperationsHistoryService = new PaymentOperationsHistoryService(paymentsHistoryDocumentsClient, categoryDocumentsClient);

            _sut = new SyncOperationsHistoryCommandHandler(
                _sender.Object,
                _logger.Object,
                paymentAccountService,
                paymentsHistoryDocumentsClient,
                paymentOperationsHistoryService);

            const int count = 50_000;
            const double maxSeconds = 30.0;

            var accountId = Guid.NewGuid();
            var events = new List<PaymentOperationEvent>(count);

            for (var i = 0; i < count; i++)
            {
                events.Add(new PaymentOperationEvent
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        OperationDay = new DateOnly(2024, 1, (i % 28) + 1),
                        Amount = 1m,
                        PaymentAccountId = accountId,
                        CategoryId = Guid.NewGuid(),
                        Comment = string.Empty,
                        ContractorId = Guid.NewGuid()
                    }
                });
            }

            var command = new SyncOperationsHistoryCommand(accountId, events);

            var sw = Stopwatch.StartNew();
            var result = await _sut.Handle(command, _ct);
            sw.Stop();

            TestContext.WriteLine(
                $"Processed {count:N0} events in {sw.Elapsed.TotalSeconds:N2}s. " +
                $"Success: {result.IsSucceeded}, Value: {result.Payload}");

            result.IsSucceeded.Should().BeTrue("processing 50k events should succeed");

            sw.Elapsed.TotalSeconds.Should()
                .BeLessThan(
                    maxSeconds,
                    $"processing {count:N0} events should not exceed {maxSeconds} seconds");
        }

        [Test]
        public async Task Rebuild_WhenHistoryChanges_ShouldRecalculateAccountBalance()
        {
            var dbOptions = new MongoDbOptions
            {
                LedgerDatabase = "sync_balance_ledger",
                PaymentAccounts = "sync_balance_accounts",
                PaymentsHistory = "sync_balance_history",
                HandBooks = "sync_balance_handbooks",
                BulkInsertChunkSize = 500,
                ConnectionString = TestContainers.MongoDbContainer.GetConnectionString()
            };

            var mongoDbOptions = Options.Create(dbOptions);
            using var paymentAccountDocumentsClient = new PaymentAccountDocumentClient(mongoDbOptions);
            using var paymentsHistoryDocumentsClient = new PaymentsHistoryDocumentsClient(mongoDbOptions);
            using var categoryDocumentsClient = new CategoryDocumentsClient(mongoDbOptions);

            var accountId = Guid.NewGuid();
            await paymentAccountDocumentsClient.InsertOneAsync(new PaymentAccount
            {
                Key = accountId,
                Agent = "agent",
                InitialBalance = 100m,
                Balance = 100m,
                Currency = "BYN",
                Description = "projection balance test",
                Type = AccountTypes.Deposit
            });

            UpdatePaymentAccountBalanceCommand capturedCommand = null;
            _sender
                .Setup(x => x.Send(It.IsAny<UpdatePaymentAccountBalanceCommand>(), _ct))
                .Callback<IRequest<Result<Guid>>, CancellationToken>((command, _) => capturedCommand = (UpdatePaymentAccountBalanceCommand)command)
                .ReturnsAsync(Result<Guid>.Succeeded(accountId));

            var paymentAccountService = new PaymentAccountService(paymentAccountDocumentsClient);
            var paymentOperationsHistoryService = new PaymentOperationsHistoryService(paymentsHistoryDocumentsClient, categoryDocumentsClient);
            _sut = new SyncOperationsHistoryCommandHandler(
                _sender.Object,
                _logger.Object,
                paymentAccountService,
                paymentsHistoryDocumentsClient,
                paymentOperationsHistoryService);

            var removedOperationId = Guid.NewGuid();
            var activeOperationId = Guid.NewGuid();
            var operationDay = new DateOnly(2024, 7, 1);
            var events = new[]
            {
                BuildPaymentEvent(accountId, removedOperationId, 25m, operationDay, PaymentEventTypes.Added, 1),
                BuildPaymentEvent(accountId, activeOperationId, 40m, operationDay.AddDays(1), PaymentEventTypes.Added, 2),
                BuildPaymentEvent(accountId, removedOperationId, 25m, operationDay, PaymentEventTypes.Removed, 3)
            };

            var result = await _sut.Handle(new SyncOperationsHistoryCommand(accountId, events), _ct);

            result.Payload.Should().Be(140m);
            capturedCommand.Should().NotBeNull();
            capturedCommand.PaymentAccountId.Should().Be(accountId);
            capturedCommand.Balance.Should().Be(140m);
        }

        private static PaymentOperationEvent BuildPaymentEvent(
            Guid accountId,
            Guid operationId,
            decimal amount,
            DateOnly operationDay,
            PaymentEventTypes eventType,
            long sequenceNumber)
        {
            return new PaymentOperationEvent
            {
                EventType = eventType,
                SequenceNumber = sequenceNumber,
                EnvelopId = Guid.Parse($"00000000-0000-0000-0000-{sequenceNumber:000000000000}"),
                Payload = new FinancialTransaction
                {
                    Key = operationId,
                    PaymentAccountId = accountId,
                    Amount = amount,
                    OperationDay = operationDay
                }
            };
        }
    }
}
