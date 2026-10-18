using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;

using static HomeBudget.Accounting.Api.IntegrationTests.DataProtection.DataProtectionControlPlaneTestDatabase;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal static class DataProtectionMigrationTestDatabase
    {
        [SuppressMessage(
            "Security",
            "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "Executes only repository-owned data-protection migration files from the test assembly output.")]
        public static async Task ApplyMigrationScriptAsync(
            TestContainersService testContainers,
            CancellationToken cancellationToken)
        {
            var migrationsFolder = Path.Combine(AppContext.BaseDirectory, "db", "migrations");
            Directory.Exists(migrationsFolder).Should().BeTrue(
                $"the integration migration folder should exist at '{migrationsFolder}'");
            await using var connection = await OpenMasterConnectionAsync(testContainers, cancellationToken);
            foreach (var migrationName in new[]
            {
                "V9__Add_data_protection_control_plane.sql",
                "V10__Add_ledger_generation_scope.sql",
                "V13__Add_portability_import_journal.sql",
                "V14__Add_coordinated_capture_state.sql",
                "V15__Add_recovery_trigger_state.sql"
            })
            {
                var migrationPath = Path.Combine(migrationsFolder, migrationName);
                File.Exists(migrationPath).Should().BeTrue(
                    $"the migration should exist at '{migrationPath}'");
                var migration = await File.ReadAllTextAsync(migrationPath, cancellationToken);
                var batches = Regex.Split(
                    migration,
                    @"^\s*GO\s*;?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);

                foreach (var batch in batches)
                {
                    if (string.IsNullOrWhiteSpace(batch))
                    {
                        continue;
                    }

                    await using var command = connection.CreateCommand();
                    command.CommandText = batch;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        public static async Task DropControlDatabaseAsync(
            TestContainersService testContainers,
            CancellationToken cancellationToken)
        {
            testContainers.IsReadyForUse.Should().BeTrue();
            testContainers.MsSqlDbContainer.Should().NotBeNull(
                "destructive migration tests are restricted to the process-owned SQL Testcontainer");
            await using var connection = await OpenMasterConnectionAsync(testContainers, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                IF DB_ID(N'{DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{DatabaseName}];
                END;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<bool> ControlDatabaseExistsAsync(
            TestContainersService testContainers,
            CancellationToken cancellationToken)
        {
            await using var connection = await OpenMasterConnectionAsync(testContainers, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN 0 ELSE 1 END;";
            command.Parameters.Add("@DatabaseName", SqlDbType.NVarChar, 128).Value = DatabaseName;
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }

        public static async Task InsertAccountingMarkerAsync(
            TestContainersService testContainers,
            Guid markerId,
            CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(testContainers.AccountingDbConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO dbo.OutboxAccountPayments
                    (Id, LedgerId, LedgerGenerationId, AggregateId, EventType, Payload, PartitionKey, MessageId,
                     CreatedAt, UpdatedAt, CreatedUtc, UpdatedUtc)
                VALUES
                    (@Id, N'default', N'legacy', N'upgrade-marker', N'UpgradeMarker', N'{}', N'upgrade-marker',
                     @MessageId, @NowUtc, @NowUtc, @NowUtc, @NowUtc);";
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = markerId;
            command.Parameters.Add("@MessageId", SqlDbType.NVarChar, 100).Value = markerId.ToString("N");
            command.Parameters.Add("@NowUtc", SqlDbType.DateTime2).Value = DateTime.UtcNow;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<bool> AccountingMarkerExistsAsync(
            TestContainersService testContainers,
            Guid markerId,
            CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(testContainers.AccountingDbConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.OutboxAccountPayments WHERE Id = @Id;";
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = markerId;
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }

        public static async Task DeleteAccountingMarkerAsync(
            TestContainersService testContainers,
            Guid markerId,
            CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(testContainers.AccountingDbConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM dbo.OutboxAccountPayments WHERE Id = @Id;";
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = markerId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<SqlConnection> OpenMasterConnectionAsync(
            TestContainersService testContainers,
            CancellationToken cancellationToken)
        {
            var builder = new SqlConnectionStringBuilder(testContainers.AccountingDbConnectionString)
            {
                InitialCatalog = "master"
            };
            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
