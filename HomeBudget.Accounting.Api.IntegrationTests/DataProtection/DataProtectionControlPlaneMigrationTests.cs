using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;

using static HomeBudget.Accounting.Api.IntegrationTests.DataProtection.DataProtectionControlPlaneTestDatabase;
using static HomeBudget.Accounting.Api.IntegrationTests.DataProtection.DataProtectionMigrationTestDatabase;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    public sealed class DataProtectionControlPlaneMigrationTests : BaseIntegrationTests
    {
        private HashSet<string> _initialSchemaObjects;

        private static readonly string[] RequiredTables =
        [
            "dbo.DataProtectionOperations",
            "dbo.DataProtectionLeases",
            "dbo.ActiveLedgerGenerations",
            "dbo.MaintenanceEpochs",
            "dbo.ComponentReceipts",
            "dbo.CapturePendingWorkDispositions",
            "dbo.RecoveryTriggerStates",
            "dbo.RecoveryTriggerDispatches",
            "dbo.PortabilityImportRuns",
            "dbo.ImportOutcomes",
            "dbo.RecoveryManifests",
            "dbo.ProtectedAuditEntries"
        ];

        private static readonly string[] RequiredSchemaObjects =
        [
            "PK_DataProtectionOperations",
            "CK_DataProtectionOperations_State",
            "CK_DataProtectionOperations_PlanFingerprint",
            "IX_DataProtectionOperations_ScopeState",
            "PK_DataProtectionLeases",
            "FK_DataProtectionLeases_Operation",
            "UX_DataProtectionLeases_ScopeKey",
            "PK_ActiveLedgerGenerations",
            "UX_ActiveLedgerGenerations_Generation",
            "UX_ActiveLedgerGenerations_GenerationToken",
            "PK_MaintenanceEpochs",
            "UX_MaintenanceEpochs_ActiveScope",
            "IX_MaintenanceEpochs_ScopeGeneration",
            "TR_MaintenanceEpochs_ImmutableBinding",
            "PK_ComponentReceipts",
            "FK_ComponentReceipts_Operation",
            "UX_ComponentReceipts_OperationComponentKind",
            "CK_ComponentReceipts_WatermarkJson",
            "IX_ComponentReceipts_Monotonic",
            "PK_CapturePendingWorkDispositions",
            "FK_CapturePendingWorkDispositions_Epoch",
            "CK_CapturePendingWorkDispositions_Drained",
            "PK_RecoveryTriggerStates",
            "CK_RecoveryTriggerStates_PendingWindow",
            "IX_RecoveryTriggerStates_Scheduled",
            "PK_RecoveryTriggerDispatches",
            "FK_RecoveryTriggerDispatches_Operation",
            "CK_RecoveryTriggerDispatches_ComponentsJson",
            "IX_RecoveryTriggerDispatches_Created",
            "PK_PortabilityImportRuns",
            "FK_PortabilityImportRuns_Operation",
            "UX_PortabilityImportRuns_ExportTarget",
            "TR_PortabilityImportRuns_ImmutableBinding",
            "PK_ImportOutcomes",
            "FK_ImportOutcomes_Operation",
            "UX_ImportOutcomes_SourceIdentity",
            "UX_ImportOutcomes_IdempotencyKey",
            "PK_RecoveryManifests",
            "FK_RecoveryManifests_Operation",
            "CK_RecoveryManifests_Json",
            "CK_RecoveryManifests_Publication",
            "IX_RecoveryManifests_ProtectedCutoff",
            "PK_ProtectedAuditEntries",
            "FK_ProtectedAuditEntries_Operation",
            "IX_ProtectedAuditEntries_OperationOccurred",
            "TR_DataProtectionOperations_ImmutableBinding",
            "TR_ImportOutcomes_ImmutableBinding",
            "TR_ProtectedAuditEntries_AppendOnly"
        ];

        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            await using var connection = await OpenAsync(TestContainers, cancellationToken, RequiredTables);
            _initialSchemaObjects = await ReadSchemaObjectNamesAsync(connection, cancellationToken);
        }

        [Test]
        public void FreshMigration_WhenFixtureStarts_HasRequiredTablesKeysConstraintsAndIndexes()
        {
            _initialSchemaObjects.Should().Contain(RequiredSchemaObjects);
        }

        [Test]
        public async Task MigrationScript_WhenAppliedAgain_IsIdempotentAndPreservesSchema()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            await using var beforeConnection = await OpenAsync(TestContainers, cancellationToken, RequiredTables);
            var before = await ReadSchemaSignatureAsync(beforeConnection, cancellationToken);
            await beforeConnection.CloseAsync();

            await ApplyMigrationScriptAsync(TestContainers, cancellationToken);
            await ApplyMigrationScriptAsync(TestContainers, cancellationToken);

            await using var afterConnection = await OpenAsync(TestContainers, cancellationToken, RequiredTables);
            var after = await ReadSchemaSignatureAsync(afterConnection, cancellationToken);
            after.Should().Be(before);
        }

        [Test]
        public async Task OperationConstraints_WhenStateOrPlanBindingIsInvalid_RejectMutation()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var operationId = Guid.NewGuid();
            const string fingerprint =
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            await using var connection = await OpenAsync(TestContainers, cancellationToken, OperationsTable);
            await InsertOperationAsync(
                connection,
                operationId,
                $"integration/environment/ledger/{Guid.NewGuid():N}",
                fingerprint,
                cancellationToken);

            var invalidState = async () => await TransitionOperationAsync(
                connection,
                operationId,
                "queued",
                fingerprint,
                "not-a-state",
                cancellationToken);
            var changedPlan = async () => await ChangePlanFingerprintAsync(
                connection,
                operationId,
                new string('a', 64),
                cancellationToken);

            await invalidState.Should().ThrowAsync<SqlException>("the lifecycle state is schema constrained");
            await changedPlan.Should().ThrowAsync<SqlException>("the accepted operation binding is immutable");
            (await ReadOperationStateAsync(connection, operationId, cancellationToken)).Should().Be("queued");
        }

        [Test]
        public async Task UpgradeMigration_WhenV0ThroughV8StateExists_AddsControlPlaneWithoutChangingAccountingState()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var markerId = Guid.NewGuid();
            await InsertAccountingMarkerAsync(TestContainers, markerId, cancellationToken);

            try
            {
                await DropControlDatabaseAsync(TestContainers, cancellationToken);
                (await ControlDatabaseExistsAsync(TestContainers, cancellationToken)).Should().BeFalse();

                await ApplyMigrationScriptAsync(TestContainers, cancellationToken);

                await using var connection = await OpenAsync(TestContainers, cancellationToken, RequiredTables);
                (await AccountingMarkerExistsAsync(TestContainers, markerId, cancellationToken)).Should().BeTrue(
                    "adding the isolated control database must not mutate existing Accounting state");
            }
            finally
            {
                await ApplyMigrationScriptAsync(TestContainers, CancellationToken.None);
                await DeleteAccountingMarkerAsync(TestContainers, markerId, CancellationToken.None);
            }
        }

        private static async Task<HashSet<string>> ReadSchemaObjectNamesAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT name FROM sys.key_constraints
                UNION ALL SELECT name FROM sys.foreign_keys
                UNION ALL SELECT name FROM sys.check_constraints
                UNION ALL SELECT name FROM sys.triggers WHERE parent_class = 1
                UNION ALL SELECT name FROM sys.indexes WHERE name IS NOT NULL;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var names = new HashSet<string>(StringComparer.Ordinal);

            while (await reader.ReadAsync(cancellationToken))
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        private static async Task<string> ReadSchemaSignatureAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT CONCAT(
                    (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0), N':',
                    (SELECT COUNT_BIG(*) FROM sys.columns
                     WHERE object_id IN (SELECT object_id FROM sys.tables WHERE is_ms_shipped = 0)), N':',
                    (SELECT COUNT_BIG(*) FROM sys.objects WHERE is_ms_shipped = 0), N':',
                    (SELECT COUNT_BIG(*) FROM sys.indexes
                     WHERE object_id IN (SELECT object_id FROM sys.tables WHERE is_ms_shipped = 0)));";
            return (string)await command.ExecuteScalarAsync(cancellationToken);
        }

        private static async Task ChangePlanFingerprintAsync(
            SqlConnection connection,
            Guid operationId,
            string fingerprint,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                UPDATE {OperationsTable}
                SET PlanFingerprint = @Fingerprint
                WHERE OperationId = @OperationId;";
            command.Parameters.Add("@Fingerprint", SqlDbType.Char, 64).Value = fingerprint;
            command.Parameters.Add("@OperationId", SqlDbType.UniqueIdentifier).Value = operationId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
