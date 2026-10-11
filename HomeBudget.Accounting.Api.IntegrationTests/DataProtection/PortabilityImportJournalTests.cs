using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import;

using static HomeBudget.Accounting.Api.IntegrationTests.DataProtection.DataProtectionControlPlaneTestDatabase;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    internal sealed class PortabilityImportJournalTests : BaseIntegrationTests
    {
        private const string Fingerprint =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Test]
        public async Task CreateOrReadAsync_WhenProcessRestarts_ReadsSameImmutableBindingAndOutcomes()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var request = await CreateRequestAsync(cancellationToken);
            var firstStore = new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString);

            var created = await firstStore.CreateOrReadAsync(request, cancellationToken);
            var retried = await firstStore.CreateOrReadAsync(request, cancellationToken);
            var restarted = await new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString)
                .GetAsync(request.OperationId, cancellationToken);

            created.PackageSha256.Should().Be(request.Preflight.PackageSha256);
            retried.Outcomes.Should().HaveCount(2);
            restarted.Should().BeEquivalentTo(created);
            (await CountAsync("dbo.PortabilityImportRuns", request.OperationId, cancellationToken)).Should().Be(1);
            (await CountAsync("dbo.ImportOutcomes", request.OperationId, cancellationToken)).Should().Be(2);
            (await CountAuditAsync(request.OperationId, cancellationToken)).Should().Be(1);
        }

        [Test]
        public async Task CreateOrReadAsync_WhenBindingChanges_ReturnsConflictWithoutMutation()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var request = await CreateRequestAsync(cancellationToken);
            var store = new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString);
            await store.CreateOrReadAsync(request, cancellationToken);
            var changed = Copy(request, packageSha256: new string('b', 64));

            var action = async () => await store.CreateOrReadAsync(changed, cancellationToken);

            (await action.Should().ThrowAsync<PortabilityImportJournalConflictException>())
                .Which.Code.Should().Be("import.journal-binding-conflict");
            (await CountAsync("dbo.ImportOutcomes", request.OperationId, cancellationToken)).Should().Be(2);
        }

        [Test]
        public async Task CreateOrReadAsync_WhenOperationBindingDiffers_RejectsBeforeJournalMutation()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var request = await CreateRequestAsync(cancellationToken, "replace");
            var store = new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString);

            var action = async () => await store.CreateOrReadAsync(request, cancellationToken);

            (await action.Should().ThrowAsync<PortabilityImportJournalConflictException>())
                .Which.Code.Should().Be("import.operation-binding-conflict");
            (await CountAsync("dbo.PortabilityImportRuns", request.OperationId, cancellationToken)).Should().Be(0);
            (await CountAsync("dbo.ImportOutcomes", request.OperationId, cancellationToken)).Should().Be(0);
        }

        [Test]
        public async Task CreateOrReadAsync_WhenEntitySetViolatesIdentityUniqueness_RollsBackWholeJournal()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var request = await CreateRequestAsync(cancellationToken);
            var duplicate = request.Preflight.Entities[0];
            var invalid = Copy(request, entities: [duplicate, duplicate]);
            var store = new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString);

            var action = async () => await store.CreateOrReadAsync(invalid, cancellationToken);

            await action.Should().ThrowAsync<PortabilityImportJournalConflictException>();
            (await CountAsync("dbo.PortabilityImportRuns", request.OperationId, cancellationToken)).Should().Be(0);
            (await CountAsync("dbo.ImportOutcomes", request.OperationId, cancellationToken)).Should().Be(0);
        }

        [Test]
        public async Task Schema_WhenRunOrEntityBindingIsUpdated_RejectsImmutableMutation()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var request = await CreateRequestAsync(cancellationToken);
            var store = new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString);
            await store.CreateOrReadAsync(request, cancellationToken);

            var runUpdate = () => UpdateAsync(
                "dbo.PortabilityImportRuns",
                "PackageSha256",
                new string('c', 64),
                request.OperationId,
                cancellationToken);
            var entityUpdate = () => UpdateAsync(
                "dbo.ImportOutcomes",
                "Fingerprint",
                new string('d', 64),
                request.OperationId,
                cancellationToken);

            await runUpdate.Should().ThrowAsync<SqlException>();
            await entityUpdate.Should().ThrowAsync<SqlException>();
        }

        [Test]
        public async Task Transitions_WhenProcessRestarts_PreserveSettledOutcomesAndAwaitingReconciliation()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var request = await CreateRequestAsync(cancellationToken);
            var store = new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString);
            var journal = await store.CreateOrReadAsync(request, cancellationToken);
            (await store.TryTransitionRunAsync(
                request.OperationId,
                "journaled",
                "executing",
                DateTime.UtcNow,
                cancellationToken)).Should().BeTrue();
            foreach (var outcome in journal.Outcomes)
            {
                (await store.TryTransitionOutcomeAsync(
                    Transition(outcome, "validated", "accepted"),
                    cancellationToken)).Should().BeTrue();
                (await store.TryTransitionOutcomeAsync(
                    Transition(outcome, "accepted", "projected"),
                    cancellationToken)).Should().BeTrue();
            }

            (await store.TryTransitionRunAsync(
                request.OperationId,
                "executing",
                "awaiting-reconciliation",
                DateTime.UtcNow,
                cancellationToken)).Should().BeTrue();

            var restarted = await new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString)
                .GetAsync(request.OperationId, cancellationToken);

            restarted.State.Should().Be("awaiting-reconciliation");
            restarted.Outcomes.Should().OnlyContain(value => value.State == "projected");
        }

        [Test]
        public async Task RecordReconciliationAsync_PersistsEvidenceAndReconciledStateAtomically()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var request = await CreateRequestAsync(cancellationToken);
            var store = new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString);
            var journal = await store.CreateOrReadAsync(request, cancellationToken);
            await store.TryTransitionRunAsync(
                request.OperationId,
                "journaled",
                "executing",
                DateTime.UtcNow,
                cancellationToken);
            foreach (var outcome in journal.Outcomes)
            {
                await store.TryTransitionOutcomeAsync(
                    Transition(outcome, "validated", "projected"),
                    cancellationToken);
            }

            await store.TryTransitionRunAsync(
                request.OperationId,
                "executing",
                "awaiting-reconciliation",
                DateTime.UtcNow,
                cancellationToken);
            var evidence = new string('e', 64);

            (await store.RecordReconciliationAsync(
                new PortabilityImportReconciliationRecord(
                    request.OperationId,
                    "integration-test-actor",
                    true,
                    evidence,
                    null,
                    DateTime.UtcNow),
                cancellationToken)).Should().BeTrue();

            var restarted = await new SqlPortabilityImportJournalStore(TestContainers.AccountingDbConnectionString)
                .GetAsync(request.OperationId, cancellationToken);
            restarted.State.Should().Be("succeeded");
            restarted.Outcomes.Should().OnlyContain(value => value.State == "reconciled");
            (await CountReconciliationAuditAsync(
                request.OperationId,
                evidence,
                cancellationToken)).Should().Be(1);
        }

        private async Task<PortabilityImportJournalRequest> CreateRequestAsync(
            CancellationToken cancellationToken,
            string operationKind = "import")
        {
            var operationId = Guid.NewGuid();
            await using var connection = await OpenAsync(TestContainers, cancellationToken, OperationsTable);
            await InsertImportOperationAsync(connection, operationId, operationKind, cancellationToken);
            return BuildRequest(operationId);
        }

        private static PortabilityImportJournalRequest BuildRequest(Guid operationId) => new()
        {
            OperationId = operationId,
            ActorId = "integration-test-actor",
            Mode = "empty-target",
            TargetEnvironmentId = "integration",
            TargetLedgerId = "default",
            TargetGenerationId = "legacy",
            TargetGenerationNumber = 0,
            CreatedUtc = DateTime.UtcNow,
            Preflight = new PortabilityImportPreflightResult(
                operationId,
                new string('a', 64),
                "1.0.0",
                "full",
                1024,
                [
                    new("accounts", "account-1", new string('1', 64), new string('2', 64), "accounts.jsonl"),
                    new("operations", "operation-1", new string('3', 64), new string('4', 64), "operations.jsonl")
                ])
        };

        private static PortabilityImportJournalRequest Copy(
            PortabilityImportJournalRequest source,
            string packageSha256 = null,
            IReadOnlyList<PortabilityImportEntityBinding> entities = null) => new()
        {
            OperationId = source.OperationId,
            ActorId = source.ActorId,
            Mode = source.Mode,
            TargetEnvironmentId = source.TargetEnvironmentId,
            TargetLedgerId = source.TargetLedgerId,
            TargetGenerationId = source.TargetGenerationId,
            TargetGenerationNumber = source.TargetGenerationNumber,
            CreatedUtc = source.CreatedUtc,
            Preflight = source.Preflight with
            {
                PackageSha256 = packageSha256 ?? source.Preflight.PackageSha256,
                Entities = entities ?? source.Preflight.Entities
            }
        };

        private static PortabilityImportOutcomeTransition Transition(
            PortabilityImportOutcome outcome,
            string expectedState,
            string nextState) => new(
                outcome.OperationId,
                outcome.EntityType,
                outcome.SourceEntityId,
                outcome.Fingerprint,
                outcome.IdempotencyKey,
                expectedState,
                nextState,
                null,
                DateTime.UtcNow);

        private static async Task InsertImportOperationAsync(
            SqlConnection connection,
            Guid operationId,
            string operationKind,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO dbo.DataProtectionOperations
                    (OperationId, OperationKind, ScopeKind, ScopeKey, EnvironmentId, LedgerId,
                     GenerationId, PlanFingerprint, State, CreatedUtc, UpdatedUtc)
                VALUES
                    (@OperationId, @OperationKind, N'ledger', @ScopeKey, N'integration', N'default',
                     N'legacy', @Fingerprint, N'validating', @NowUtc, @NowUtc);";
            command.Parameters.Add("@OperationId", SqlDbType.UniqueIdentifier).Value = operationId;
            command.Parameters.Add("@OperationKind", SqlDbType.NVarChar, 64).Value = operationKind;
            command.Parameters.Add("@ScopeKey", SqlDbType.NVarChar, 256).Value = $"integration/import/{operationId:N}";
            command.Parameters.Add("@Fingerprint", SqlDbType.Char, 64).Value = Fingerprint;
            command.Parameters.Add("@NowUtc", SqlDbType.DateTime2).Value = DateTime.UtcNow;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        [SuppressMessage(
            "Security",
            "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "The switch maps the table discriminator to fixed SQL statements; all values are parameterized.")]
        private async Task<int> CountAsync(string table, Guid operationId, CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(TestContainers, cancellationToken, "dbo.PortabilityImportRuns");
            await using var command = connection.CreateCommand();
            command.CommandText = table switch
            {
                "dbo.PortabilityImportRuns" =>
                    "SELECT COUNT(1) FROM dbo.PortabilityImportRuns WHERE OperationId = @OperationId;",
                "dbo.ImportOutcomes" =>
                    "SELECT COUNT(1) FROM dbo.ImportOutcomes WHERE OperationId = @OperationId;",
                _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unsupported table.")
            };
            command.Parameters.Add("@OperationId", SqlDbType.UniqueIdentifier).Value = operationId;
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        private async Task<int> CountAuditAsync(Guid operationId, CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(TestContainers, cancellationToken, "dbo.PortabilityImportRuns");
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(1) FROM dbo.ProtectedAuditEntries
                 WHERE OperationId = @OperationId AND EventType = N'import-journal-created';";
            command.Parameters.Add("@OperationId", SqlDbType.UniqueIdentifier).Value = operationId;
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        private async Task<int> CountReconciliationAuditAsync(
            Guid operationId,
            string evidence,
            CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(TestContainers, cancellationToken, "dbo.PortabilityImportRuns");
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(1) FROM dbo.ProtectedAuditEntries
                 WHERE OperationId = @OperationId
                   AND EventType = N'import-reconciliation'
                   AND SourceChecksum = @Evidence
                   AND Result = N'succeeded';";
            command.Parameters.Add("@OperationId", SqlDbType.UniqueIdentifier).Value = operationId;
            command.Parameters.Add("@Evidence", SqlDbType.Char, 64).Value = evidence;
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        [SuppressMessage(
            "Security",
            "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "The table and column tuple is allowlisted to fixed SQL statements; all values are parameterized.")]
        private async Task UpdateAsync(
            string table,
            string column,
            string value,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            await using var connection = await OpenAsync(TestContainers, cancellationToken, "dbo.PortabilityImportRuns");
            await using var command = connection.CreateCommand();
            command.CommandText = (table, column) switch
            {
                ("dbo.PortabilityImportRuns", "PackageSha256") =>
                    "UPDATE dbo.PortabilityImportRuns SET PackageSha256 = @Value WHERE OperationId = @OperationId;",
                ("dbo.ImportOutcomes", "Fingerprint") =>
                    "UPDATE dbo.ImportOutcomes SET Fingerprint = @Value WHERE OperationId = @OperationId;",
                _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unsupported update.")
            };
            command.Parameters.Add("@Value", SqlDbType.Char, 64).Value = value;
            command.Parameters.Add("@OperationId", SqlDbType.UniqueIdentifier).Value = operationId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
