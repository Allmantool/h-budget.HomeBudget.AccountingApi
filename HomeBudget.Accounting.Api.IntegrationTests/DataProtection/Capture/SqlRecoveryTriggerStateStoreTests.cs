using System;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Infrastructure.DataProtection.Triggers;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection.Capture
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    public sealed class SqlRecoveryTriggerStateStoreTests : BaseIntegrationTests
    {
        [Test]
        public async Task CoalescedChanges_SurviveStoreRestartAndAtomicallyEnqueueAtBound()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
            var target = CreateTarget();
            var options = new RecoveryTriggerOptions
            {
                RecordThreshold = 100,
                ByteThreshold = 1000,
                MaximumCoalescingDelay = TimeSpan.FromMinutes(10)
            };
            var first = CreateCoordinator(options);

            (await first.RecordChangesAsync(target, 1, 10, now, cancellationToken))
                .WasTriggered.Should().BeFalse();

            var restarted = CreateCoordinator(options);
            var result = await restarted.RecordChangesAsync(
                target,
                0,
                0,
                now.AddMinutes(10),
                cancellationToken);

            result.WasTriggered.Should().BeTrue();
            result.OperationId.Should().NotBeNull();
            await using var connection = CreateControlConnection();
            var dispatch = await connection.QuerySingleAsync<DispatchRow>(new CommandDefinition(
                @"SELECT TriggerReason, RecordCount, ByteCount
                    FROM dbo.RecoveryTriggerDispatches
                   WHERE OperationId = @OperationId;",
                new { result.OperationId },
                cancellationToken: cancellationToken));
            dispatch.TriggerReason.Should().Contain("bounded-coalescing");
            dispatch.RecordCount.Should().Be(1);
            dispatch.ByteCount.Should().Be(10);
            (await connection.QuerySingleAsync<int>(new CommandDefinition(
                @"SELECT COUNT(1) FROM dbo.DataProtectionOperations
                   WHERE OperationId = @OperationId AND OperationKind = N'backup' AND State = N'queued';",
                new { result.OperationId },
                cancellationToken: cancellationToken))).Should().Be(1);
        }

        [Test]
        public async Task NamedBulk_RepeatedWithNoChanges_AlwaysCreatesDistinctDurableOperations()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
            var target = CreateTarget();
            var coordinator = CreateCoordinator(new RecoveryTriggerOptions());

            var first = await coordinator.TriggerNamedBulkAsync(
                target,
                "family-pro-12/run-42",
                now,
                cancellationToken);
            var second = await coordinator.TriggerNamedBulkAsync(
                target,
                "family-pro-12/run-42",
                now.AddSeconds(1),
                cancellationToken);

            first.OperationId.Should().NotBeNull();
            second.OperationId.Should().NotBeNull();
            first.OperationId.Value.Should().NotBe(second.OperationId.Value);
            await using var connection = CreateControlConnection();
            (await connection.QuerySingleAsync<int>(new CommandDefinition(
                @"SELECT COUNT(1) FROM dbo.RecoveryTriggerDispatches
                   WHERE OperationId IN @OperationIds
                     AND TriggerKind = N'named-bulk'
                     AND TriggerReason = N'named-bulk:family-pro-12/run-42';",
                new { OperationIds = new[] { first.OperationId.Value, second.OperationId.Value } },
                cancellationToken: cancellationToken))).Should().Be(2);
        }

        private RecoveryTriggerCoordinator CreateCoordinator(RecoveryTriggerOptions options) => new(
            new SqlRecoveryTriggerStateStore(TestContainers.AccountingDbConnectionString),
            new RecoveryTriggerPolicy(options));

        private static RecoveryTriggerTarget CreateTarget() => new(
            "integration",
            "default",
            "legacy",
            $"ledger:integration:{Guid.NewGuid():N}",
            "integration-trigger",
            "test-key-reference",
            new[] { "accounting-sql", "eventstore", "mongo", "rates-sql" });

        private SqlConnection CreateControlConnection()
        {
            var builder = new SqlConnectionStringBuilder(TestContainers.AccountingDbConnectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            };
            return new SqlConnection(builder.ConnectionString);
        }

        private sealed class DispatchRow
        {
            public string TriggerReason { get; set; }
            public long RecordCount { get; set; }
            public long ByteCount { get; set; }
        }
    }
}
