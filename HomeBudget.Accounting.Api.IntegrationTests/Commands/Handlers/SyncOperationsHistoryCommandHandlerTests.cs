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
    [Order(IntegrationTestOrderIndex.SyncOperationsHistoryHighLoadTests)]
    public class SyncOperationsHistoryCommandHandlerTests : BaseIntegrationTests
    {
        private Mock<ISender> _sender;
        private Mock<ILogger<SyncOperationsHistoryCommandHandler>> _logger;

        private CancellationToken _ct;

        private SyncOperationsHistoryCommandHandler _sut;

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
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
    }
}
