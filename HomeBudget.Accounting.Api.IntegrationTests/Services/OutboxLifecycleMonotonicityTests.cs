using System;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.SqlClients;
using HomeBudget.Accounting.Infrastructure.Data.SqlClients.MsSql;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Services
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    public class OutboxLifecycleMonotonicityTests : BaseIntegrationTests
    {
        [Test]
        public async Task LifecycleUpdates_WhenOlderTransitionsArriveAfterProjected_ThenDurableStatusDoesNotRegress()
        {
            var service = CreateService();
            var accountId = Guid.NewGuid();
            var commandId = Guid.NewGuid().ToString();
            var nowUtc = DateTime.UtcNow;

            await service.WriteRecordAsync(CreateOutboxRow(accountId, commandId, nowUtc));
            await service.MarkProjectedAsync(commandId, nowUtc.AddSeconds(1));
            var projectedCommand = await service.GetCommandAsync(accountId, commandId);
            await service.MarkProjectedAsync(commandId, nowUtc.AddSeconds(2));
            await service.MarkPersistedAsync(commandId, nowUtc.AddSeconds(2));
            await service.SetStatusAsync(commandId, OutboxStatus.Published);
            await service.SetStatusAsync(commandId, OutboxStatus.Pending);

            var command = await service.GetCommandAsync(accountId, commandId);

            command.Status.Should().Be(PaymentCommandStatus.Projected);
            command.PersistedUtc.Should().BeNull();
            command.ProjectedUtc.Should().Be(projectedCommand.ProjectedUtc);
        }

        [Test]
        public async Task LifecycleUpdates_WhenDelayedDeadLetterFollowsSuccessfulTerminalState_ThenProjectedStateWins()
        {
            var service = CreateService();
            var accountId = Guid.NewGuid();
            var commandId = Guid.NewGuid().ToString();
            var nowUtc = DateTime.UtcNow;
            const string lockedBy = "delayed-failure-test";

            await service.WriteRecordAsync(CreateOutboxRow(accountId, commandId, nowUtc));
            await service.LockRetryableRowsAsync(
                lockedBy,
                nowUtc,
                nowUtc.AddMinutes(1),
                batchSize: 1,
                maxRetryAttempts: 3);
            await service.MarkPersistedAsync(commandId, nowUtc.AddSeconds(1));
            await service.MarkProjectedAsync(commandId, nowUtc.AddSeconds(2));
            await service.MarkFailedAsync(commandId, lockedBy, "delayed retry failure", 3, nowUtc.AddSeconds(3));
            await service.MarkDeadLetteredAsync(commandId, "delayed failure", nowUtc.AddSeconds(3));

            var command = await service.GetCommandAsync(accountId, commandId);

            command.Status.Should().Be(PaymentCommandStatus.Projected);
        }

        [Test]
        public async Task LifecycleUpdates_WhenOlderAndNewerTransitionsRace_ThenDurableStatusConvergesToProjected()
        {
            var service = CreateService();
            var accountId = Guid.NewGuid();
            var commandId = Guid.NewGuid().ToString();
            var nowUtc = DateTime.UtcNow;

            await service.WriteRecordAsync(CreateOutboxRow(accountId, commandId, nowUtc));

            await Task.WhenAll(
                service.SetStatusAsync(commandId, OutboxStatus.Published),
                service.MarkPersistedAsync(commandId, nowUtc.AddSeconds(1)),
                service.MarkProjectedAsync(commandId, nowUtc.AddSeconds(2)),
                service.MarkPersistedAsync(commandId, nowUtc.AddSeconds(3)));

            var command = await service.GetCommandAsync(accountId, commandId);

            command.Status.Should().Be(PaymentCommandStatus.Projected);
            command.ProjectedUtc.Should().NotBeNull();
        }

        private OutboxPaymentStatusService CreateService()
        {
            var databaseOptions = Options.Create(new DatabaseConnectionOptions
            {
                ConnectionString = TestContainers.AccountingDbConnectionString,
                SqlReadCommandTimeoutSeconds = 30,
                SqlWriteCommandTimeoutSeconds = 30
            });
            var connectionFactory = new SqlConnectionFactory(
                Mock.Of<ILogger<SqlConnectionFactory>>(),
                databaseOptions);
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(x => x.GetNowUtc()).Returns(DateTime.UtcNow);

            return new OutboxPaymentStatusService(
                Mock.Of<ILogger<OutboxPaymentStatusService>>(),
                dateTimeProvider.Object,
                new DapperWriteRepository(connectionFactory, databaseOptions),
                new DapperReadRepository(connectionFactory, databaseOptions));
        }

        private static OutboxAccountPaymentsEntity CreateOutboxRow(Guid accountId, string commandId, DateTime nowUtc)
        {
            return new OutboxAccountPaymentsEntity
            {
                AggregateId = accountId.ToString(),
                OperationId = Guid.NewGuid().ToString(),
                EventType = "Added",
                MessageId = commandId,
                PartitionKey = accountId.ToString(),
                Payload = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };
        }
    }
}
