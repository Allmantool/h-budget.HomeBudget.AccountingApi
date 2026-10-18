using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Data.SqlClient;

using HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection.SqlServer
{
    internal sealed class DisposableSqlServerProtectionTarget : IAsyncDisposable
    {
        private const string DatabasePrefix = "HomeLedgerProtectionTest_";
        private const string BackupRoot = "/var/opt/mssql/data";
        private const string KnownTestPassword = "Passw0rd!";

        private readonly TestContainersService _containers;
        private readonly string _containerId;
        private readonly string _connectionString;
        private readonly SqlServerProtectionIdentity _identity;

        private DisposableSqlServerProtectionTarget(
            TestContainersService containers,
            string containerId,
            string connectionString,
            SqlServerProtectionIdentity identity,
            string databaseName)
        {
            _containers = containers;
            _containerId = containerId;
            _connectionString = connectionString;
            _identity = identity;
            DatabaseName = databaseName;
        }

        public string DatabaseName { get; }

        public string PasswordCanary => KnownTestPassword;

        public static async Task<DisposableSqlServerProtectionTarget> CreateAsync(
            TestContainersService containers,
            CancellationToken cancellationToken)
        {
            containers.Should().NotBeNull();
            containers.IsReadyForUse.Should().BeTrue();
            containers.MsSqlDbContainer.Should().NotBeNull();
            containers.MsSqlDbContainer.Id.Should().NotBeNullOrWhiteSpace();

            var mount = await containers.MsSqlDbContainer.ExecAsync(
                new[] { "sh", "-c", "findmnt -T /var/opt/mssql/data -n -o FSTYPE" },
                cancellationToken);
            mount.ExitCode.Should().Be(0);
            mount.Stdout.Trim().Should().Be("overlay", "the test backup target must not be a host-mounted volume");

            var databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
            var builder = new SqlConnectionStringBuilder(containers.AccountingDbConnectionString)
            {
                InitialCatalog = "master",
                PersistSecurityInfo = false
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            var identity = await ReadIdentityAsync(connection, cancellationToken);
            containers.MsSqlDbContainer.Id.Should().StartWith(
                identity.MachineName,
                "the SQL target must be the positively identified Testcontainers instance");

            await ExecuteAsync(
                connection,
                $"CREATE DATABASE [{databaseName}]; ALTER DATABASE [{databaseName}] SET RECOVERY FULL;",
                cancellationToken);

            return new DisposableSqlServerProtectionTarget(
                containers,
                containers.MsSqlDbContainer.Id,
                builder.ConnectionString,
                identity,
                databaseName);
        }

        public SqlServerProtectionProvider CreateProvider(string expectedProductVersion = null)
        {
            var policy = new SqlServerProtectionPolicy(
                DatabaseName,
                BackupRoot,
                _identity.ServerName,
                expectedProductVersion ?? _identity.ProductVersion,
                _identity.Edition,
                "FULL",
                new[] { DatabaseName },
                new[] { BackupRoot },
                120);

            return new SqlServerProtectionProvider(_connectionString, policy);
        }

        public async Task ExecuteDatabaseCommandAsync(string commandText, CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(BuildDatabaseConnectionString());
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, commandText, cancellationToken);
        }

        public async Task<DateTimeOffset> ReadServerUtcNowAsync(CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT SYSDATETIMEOFFSET();";
            return (DateTimeOffset)await command.ExecuteScalarAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            DatabaseName.Should().StartWith(DatabasePrefix);
            _containers.MsSqlDbContainer.Id.Should().Be(_containerId);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            var identity = await ReadIdentityAsync(connection, CancellationToken.None);
            identity.ServerName.Should().Be(_identity.ServerName);
            await ExecuteAsync(
                connection,
                $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}];",
                CancellationToken.None);
        }

        private string BuildDatabaseConnectionString()
        {
            var builder = new SqlConnectionStringBuilder(_connectionString)
            {
                InitialCatalog = DatabaseName
            };
            return builder.ConnectionString;
        }

        private static async Task<SqlServerProtectionIdentity> ReadIdentityAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    CONVERT(nvarchar(128), SERVERPROPERTY('MachineName')),
                    CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')),
                    CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')),
                    CONVERT(nvarchar(128), SERVERPROPERTY('Edition'));";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            (await reader.ReadAsync(cancellationToken)).Should().BeTrue();
            return new SqlServerProtectionIdentity(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3));
        }

        [SuppressMessage(
            "Security",
            "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "This private disposable Testcontainers helper accepts only test-owned commands after positive container identity verification.")]
        private static async Task ExecuteAsync(
            SqlConnection connection,
            string commandText,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private sealed record SqlServerProtectionIdentity(
            string MachineName,
            string ServerName,
            string ProductVersion,
            string Edition);
    }
}
