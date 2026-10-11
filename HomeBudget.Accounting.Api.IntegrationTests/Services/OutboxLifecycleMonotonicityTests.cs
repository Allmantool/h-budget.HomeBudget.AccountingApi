using System;
using System.Data;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.DataProtection;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.SqlClients;
using HomeBudget.Accounting.Infrastructure.Data.SqlClients.MsSql;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Services
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
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

        [Test]
        public async Task CommandAcceptance_WhenActiveGenerationChangesAfterAdmission_ThenSqlRejectsTheStaleCommand()
        {
            var service = CreateService();
            var commandId = Guid.NewGuid().ToString("N");
            var staleAdmission = CreateOutboxRow(Guid.NewGuid(), commandId, DateTime.UtcNow);
            var replacementToken = $"race-{Guid.NewGuid():N}";

            await using var controlConnection = await DataProtectionControlPlaneTestDatabase.OpenAsync(
                TestContainers,
                TestContext.CurrentContext.CancellationToken,
                "dbo.ActiveLedgerGenerations");

            try
            {
                await SetActiveGenerationAsync(controlConnection, "replacement-race", replacementToken, 1);

                var accept = async () => await service.WriteRecordAsync(staleAdmission);

                await accept.Should().ThrowAsync<LedgerGenerationChangedException>(
                    "the active generation is revalidated in the same SQL transaction as durable acceptance");
                (await CountOutboxRowsAsync(commandId)).Should().Be(
                    0,
                    "a request admitted under a generation that became inactive must have no durable financial effect");
            }
            finally
            {
                await SetActiveGenerationAsync(controlConnection, "legacy", "legacy-token-v1", 0);
            }
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

        private static async Task SetActiveGenerationAsync(
            SqlConnection connection,
            string generationId,
            string generationToken,
            long generationNumber)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE dbo.ActiveLedgerGenerations
                SET GenerationId = @GenerationId,
                    GenerationToken = @GenerationToken,
                    GenerationNumber = @GenerationNumber,
                    PreviousGenerationId = CASE WHEN @GenerationNumber = 0 THEN NULL ELSE N'legacy' END,
                    ActivatedUtc = SYSUTCDATETIME()
                WHERE EnvironmentId = N'default'
                  AND LedgerId = N'default';";
            command.Parameters.Add("@GenerationId", SqlDbType.NVarChar, 128).Value = generationId;
            command.Parameters.Add("@GenerationToken", SqlDbType.NVarChar, 200).Value = generationToken;
            command.Parameters.Add("@GenerationNumber", SqlDbType.BigInt).Value = generationNumber;
            (await command.ExecuteNonQueryAsync(TestContext.CurrentContext.CancellationToken)).Should().Be(1);
        }

        private async Task<int> CountOutboxRowsAsync(string commandId)
        {
            await using var connection = new SqlConnection(TestContainers.AccountingDbConnectionString);
            await connection.OpenAsync(TestContext.CurrentContext.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.OutboxAccountPayments WHERE MessageId = @CommandId;";
            command.Parameters.Add("@CommandId", SqlDbType.NVarChar, 100).Value = commandId;
            return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.CurrentContext.CancellationToken));
        }
    }
}
