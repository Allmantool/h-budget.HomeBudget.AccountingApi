using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal static class DataProtectionControlPlaneTestDatabase
    {
        internal const string DatabaseName = "HomeBudget.DataProtection";
        internal const string OperationsTable = "dbo.DataProtectionOperations";
        internal const string LeasesTable = "dbo.DataProtectionLeases";
        internal const string AuditTable = "dbo.ProtectedAuditEntries";

        public static async Task<SqlConnection> OpenAsync(
            TestContainersService testContainers,
            CancellationToken cancellationToken,
            params string[] requiredTables)
        {
            Assert.That(testContainers, Is.Not.Null, "The process-owned Testcontainers fixture was not initialized.");
            Assert.That(testContainers.IsReadyForUse, Is.True, "The process-owned Testcontainers fixture is not ready.");
            Assert.That(testContainers.MsSqlDbContainer, Is.Not.Null, "The disposable SQL Testcontainer is unavailable.");

            var builder = new SqlConnectionStringBuilder(testContainers.AccountingDbConnectionString)
            {
                InitialCatalog = "master"
            };

            await using (var master = new SqlConnection(builder.ConnectionString))
            {
                await master.OpenAsync(cancellationToken);
                var exists = await DatabaseExistsAsync(master, cancellationToken);
                Assert.That(
                    exists,
                    Is.True,
                    $"Missing data-protection control database '{DatabaseName}'. " +
                    "Root OpenSpec task 1.2 has not supplied the control-plane schema.");
            }

            builder.InitialCatalog = DatabaseName;
            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            try
            {
                foreach (var table in requiredTables)
                {
                    await AssertTableExistsAsync(connection, table, cancellationToken);
                }
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            return connection;
        }

        public static async Task InsertOperationAsync(
            SqlConnection connection,
            Guid operationId,
            string scopeKey,
            string planFingerprint,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                INSERT INTO {OperationsTable}
                    (OperationId, OperationKind, ScopeKind, ScopeKey, EnvironmentId, LedgerId,
                     GenerationId, PlanFingerprint, State, CreatedUtc, UpdatedUtc)
                VALUES
                    (@OperationId, N'ledger-reset', N'ledger', @ScopeKey, N'integration', N'default',
                     N'legacy', @PlanFingerprint, N'queued', @NowUtc, @NowUtc);";
            AddParameter(command, "@OperationId", SqlDbType.UniqueIdentifier, operationId);
            AddParameter(command, "@ScopeKey", SqlDbType.NVarChar, scopeKey, 256);
            AddParameter(command, "@PlanFingerprint", SqlDbType.Char, planFingerprint, 64);
            AddParameter(command, "@NowUtc", SqlDbType.DateTime2, DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<int> TransitionOperationAsync(
            SqlConnection connection,
            Guid operationId,
            string expectedState,
            string expectedPlanFingerprint,
            string nextState,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                UPDATE {OperationsTable}
                SET State = @NextState, UpdatedUtc = @NowUtc
                WHERE OperationId = @OperationId
                  AND State = @ExpectedState
                  AND PlanFingerprint = @ExpectedPlanFingerprint;";
            AddParameter(command, "@OperationId", SqlDbType.UniqueIdentifier, operationId);
            AddParameter(command, "@ExpectedState", SqlDbType.NVarChar, expectedState, 32);
            AddParameter(command, "@ExpectedPlanFingerprint", SqlDbType.Char, expectedPlanFingerprint, 64);
            AddParameter(command, "@NextState", SqlDbType.NVarChar, nextState, 32);
            AddParameter(command, "@NowUtc", SqlDbType.DateTime2, DateTime.UtcNow);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<string> ReadOperationStateAsync(
            SqlConnection connection,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT State FROM {OperationsTable} WHERE OperationId = @OperationId;";
            AddParameter(command, "@OperationId", SqlDbType.UniqueIdentifier, operationId);
            return (string)await command.ExecuteScalarAsync(cancellationToken);
        }

        public static async Task AppendAuditAsync(
            SqlConnection connection,
            Guid auditId,
            Guid operationId,
            string scopeKey,
            string planFingerprint,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                INSERT INTO {AuditTable}
                    (AuditId, OperationId, ActorId, EventType, ScopeKey, PlanFingerprint, Result, OccurredUtc)
                VALUES
                    (@AuditId, @OperationId, N'integration-test-actor', N'operation-created',
                     @ScopeKey, @PlanFingerprint, N'accepted', @OccurredUtc);";
            AddParameter(command, "@AuditId", SqlDbType.UniqueIdentifier, auditId);
            AddParameter(command, "@OperationId", SqlDbType.UniqueIdentifier, operationId);
            AddParameter(command, "@ScopeKey", SqlDbType.NVarChar, scopeKey, 256);
            AddParameter(command, "@PlanFingerprint", SqlDbType.Char, planFingerprint, 64);
            AddParameter(command, "@OccurredUtc", SqlDbType.DateTime2, DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<int> CountRowsAsync(
            SqlConnection connection,
            string table,
            string keyColumn,
            Guid key,
            CancellationToken cancellationToken)
        {
            if (table == OperationsTable && keyColumn == "OperationId")
            {
                return await CountOperationsAsync(connection, key, cancellationToken);
            }

            if (table == AuditTable && keyColumn == "AuditId")
            {
                return await CountAuditsAsync(connection, key, cancellationToken);
            }

            throw new ArgumentOutOfRangeException(
                nameof(table),
                "The test attempted to query a table/key pair outside the fixed control-plane allowlist.");
        }

        private static async Task<int> CountOperationsAsync(
            SqlConnection connection,
            Guid key,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(1) FROM dbo.DataProtectionOperations WHERE OperationId = @Key;";
            AddParameter(command, "@Key", SqlDbType.UniqueIdentifier, key);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        private static async Task<int> CountAuditsAsync(
            SqlConnection connection,
            Guid key,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(1) FROM dbo.ProtectedAuditEntries WHERE AuditId = @Key;";
            AddParameter(command, "@Key", SqlDbType.UniqueIdentifier, key);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        private static async Task<bool> DatabaseExistsAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN 0 ELSE 1 END;";
            AddParameter(command, "@DatabaseName", SqlDbType.NVarChar, DatabaseName, 128);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }

        private static async Task AssertTableExistsAsync(
            SqlConnection connection,
            string table,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CASE WHEN OBJECT_ID(@TableName, N'U') IS NULL THEN 0 ELSE 1 END;";
            AddParameter(command, "@TableName", SqlDbType.NVarChar, table, 256);
            var exists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
            Assert.That(exists, Is.True, $"Missing required data-protection control table '{table}'.");
        }

        private static void AddParameter(
            SqlCommand command,
            string name,
            SqlDbType type,
            object value,
            int size = 0)
        {
            var parameter = size > 0
                ? command.Parameters.Add(name, type, size)
                : command.Parameters.Add(name, type);
            parameter.Value = value;
        }
    }
}
