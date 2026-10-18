using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;

using static HomeBudget.Accounting.Api.IntegrationTests.DataProtection.DataProtectionControlPlaneTestDatabase;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    public sealed class DataProtectionControlPlanePersistenceTests : BaseIntegrationTests
    {
        private const string PlanFingerprint =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Test]
        public async Task OperationTransition_WhenCommitted_SurvivesReconnectAndRejectsStaleState()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var operationId = Guid.NewGuid();
            var scopeKey = CreateScopeKey();

            await using (var connection = await OpenAsync(TestContainers, cancellationToken, OperationsTable))
            {
                await InsertOperationAsync(connection, operationId, scopeKey, PlanFingerprint, cancellationToken);
                var changed = await TransitionOperationAsync(
                    connection,
                    operationId,
                    "queued",
                    PlanFingerprint,
                    "running",
                    cancellationToken);
                changed.Should().Be(1);
            }

            await using var reconnected = await OpenAsync(TestContainers, cancellationToken, OperationsTable);
            var durableState = await ReadOperationStateAsync(reconnected, operationId, cancellationToken);
            var staleChange = await TransitionOperationAsync(
                reconnected,
                operationId,
                "queued",
                PlanFingerprint,
                "failed",
                cancellationToken);

            durableState.Should().Be("running");
            staleChange.Should().Be(0, "a stale worker must not overwrite a later durable state");
            (await ReadOperationStateAsync(reconnected, operationId, cancellationToken)).Should().Be("running");
        }

        [Test]
        public async Task LeaseAcquisition_WhenScopeAlreadyOwned_RejectsSecondReplica()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var scopeKey = CreateScopeKey();
            var operationId = Guid.NewGuid();
            await using var connection = await OpenAsync(
                TestContainers,
                cancellationToken,
                OperationsTable,
                LeasesTable);
            await InsertOperationAsync(connection, operationId, scopeKey, PlanFingerprint, cancellationToken);

            await InsertLeaseAsync(
                connection,
                Guid.NewGuid(),
                operationId,
                scopeKey,
                "replica-a",
                cancellationToken);
            var secondAcquire = async () =>
                await InsertLeaseAsync(
                    connection,
                    Guid.NewGuid(),
                    operationId,
                    scopeKey,
                    "replica-b",
                    cancellationToken);

            await secondAcquire.Should().ThrowAsync<SqlException>(
                "a durable unique scope fence must exclude a second replica");
            (await ReadLeaseOwnerAsync(connection, scopeKey, cancellationToken)).Should().Be("replica-a");
        }

        [Test]
        public async Task OperationStart_WhenPlanFingerprintIsStale_IsRejectedWithoutStateChange()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var operationId = Guid.NewGuid();
            await using var connection = await OpenAsync(TestContainers, cancellationToken, OperationsTable);
            await InsertOperationAsync(
                connection,
                operationId,
                CreateScopeKey(),
                PlanFingerprint,
                cancellationToken);

            var staleFingerprint = new string('f', 64);
            var changed = await TransitionOperationAsync(
                connection,
                operationId,
                "queued",
                staleFingerprint,
                "running",
                cancellationToken);

            changed.Should().Be(0, "execution must remain bound to the persisted plan fingerprint");
            (await ReadOperationStateAsync(connection, operationId, cancellationToken)).Should().Be("queued");
        }

        [Test]
        public async Task ProtectedAudit_WhenUpdateOrDeleteIsAttempted_RemainsImmutable()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var operationId = Guid.NewGuid();
            var auditId = Guid.NewGuid();
            var scopeKey = CreateScopeKey();
            await using var connection = await OpenAsync(
                TestContainers,
                cancellationToken,
                OperationsTable,
                AuditTable);
            await InsertOperationAsync(connection, operationId, scopeKey, PlanFingerprint, cancellationToken);
            await AppendAuditAsync(
                connection,
                auditId,
                operationId,
                scopeKey,
                PlanFingerprint,
                cancellationToken);

            var update = async () => await MutateAuditAsync(connection, auditId, delete: false, cancellationToken);
            var delete = async () => await MutateAuditAsync(connection, auditId, delete: true, cancellationToken);

            await update.Should().ThrowAsync<SqlException>("protected audit rows are append-only");
            await delete.Should().ThrowAsync<SqlException>("protected audit rows cannot be removed by ledger reset code");
            (await ReadAuditResultAsync(connection, auditId, cancellationToken)).Should().Be("accepted");
        }

        [Test]
        public async Task LedgerReset_WhenControlRowsExist_PreservesOperationAndProtectedAudit()
        {
            var cancellationToken = TestContext.CurrentContext.CancellationToken;
            var operationId = Guid.NewGuid();
            var auditId = Guid.NewGuid();
            var scopeKey = CreateScopeKey();
            await using var connection = await OpenAsync(
                TestContainers,
                cancellationToken,
                OperationsTable,
                AuditTable);
            await InsertOperationAsync(connection, operationId, scopeKey, PlanFingerprint, cancellationToken);
            await AppendAuditAsync(
                connection,
                auditId,
                operationId,
                scopeKey,
                PlanFingerprint,
                cancellationToken);

            await TestContainers.MsSqlDbContainer.ResetContainersAsync();

            (await CountRowsAsync(
                connection,
                OperationsTable,
                "OperationId",
                operationId,
                cancellationToken)).Should().Be(1);
            (await CountRowsAsync(
                connection,
                AuditTable,
                "AuditId",
                auditId,
                cancellationToken)).Should().Be(1);
        }

        private static string CreateScopeKey()
        {
            return $"integration/environment/ledger/{Guid.NewGuid():N}";
        }

        private static async Task InsertLeaseAsync(
            SqlConnection connection,
            Guid leaseId,
            Guid operationId,
            string scopeKey,
            string ownerId,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                INSERT INTO {LeasesTable}
                    (LeaseId, OperationId, ScopeKey, OwnerId, AcquiredUtc, RenewedUtc, ExpiresUtc)
                VALUES
                    (@LeaseId, @OperationId, @ScopeKey, @OwnerId, @AcquiredUtc, @AcquiredUtc, @ExpiresUtc);";
            command.Parameters.Add("@LeaseId", SqlDbType.UniqueIdentifier).Value = leaseId;
            command.Parameters.Add("@OperationId", SqlDbType.UniqueIdentifier).Value = operationId;
            command.Parameters.Add("@ScopeKey", SqlDbType.NVarChar, 256).Value = scopeKey;
            command.Parameters.Add("@OwnerId", SqlDbType.NVarChar, 128).Value = ownerId;
            command.Parameters.Add("@AcquiredUtc", SqlDbType.DateTime2).Value = DateTime.UtcNow;
            command.Parameters.Add("@ExpiresUtc", SqlDbType.DateTime2).Value = DateTime.UtcNow.AddMinutes(5);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<string> ReadLeaseOwnerAsync(
            SqlConnection connection,
            string scopeKey,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT OwnerId FROM {LeasesTable} WHERE ScopeKey = @ScopeKey;";
            command.Parameters.Add("@ScopeKey", SqlDbType.NVarChar, 256).Value = scopeKey;
            return (string)await command.ExecuteScalarAsync(cancellationToken);
        }

        private static async Task MutateAuditAsync(
            SqlConnection connection,
            Guid auditId,
            bool delete,
            CancellationToken cancellationToken)
        {
            if (delete)
            {
                await DeleteAuditAsync(connection, auditId, cancellationToken);
                return;
            }

            await UpdateAuditAsync(connection, auditId, cancellationToken);
        }

        private static async Task DeleteAuditAsync(
            SqlConnection connection,
            Guid auditId,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM dbo.ProtectedAuditEntries WHERE AuditId = @AuditId;";
            command.Parameters.Add("@AuditId", SqlDbType.UniqueIdentifier).Value = auditId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task UpdateAuditAsync(
            SqlConnection connection,
            Guid auditId,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE dbo.ProtectedAuditEntries SET Result = N'tampered' WHERE AuditId = @AuditId;";
            command.Parameters.Add("@AuditId", SqlDbType.UniqueIdentifier).Value = auditId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<string> ReadAuditResultAsync(
            SqlConnection connection,
            Guid auditId,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Result FROM {AuditTable} WHERE AuditId = @AuditId;";
            command.Parameters.Add("@AuditId", SqlDbType.UniqueIdentifier).Value = auditId;
            return (string)await command.ExecuteScalarAsync(cancellationToken);
        }
    }
}
