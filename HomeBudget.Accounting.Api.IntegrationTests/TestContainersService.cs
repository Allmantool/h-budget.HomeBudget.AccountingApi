using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using EvolveDb;
using EvolveDb.Configuration;
using Microsoft.Data.SqlClient;
using Serilog;
using Testcontainers.EventStoreDb;
using Testcontainers.Kafka;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.Factories;
using HomeBudget.Accounting.Infrastructure;
using HomeBudget.Core.Constants;
using HomeBudget.Test.Core.Factories;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    internal sealed class TestContainersService : IAsyncDisposable
    {
        private const string HomeBudgetDatabaseName = "HomeBudget";
        private const string AccountingDatabaseName = "HomeBudget.Accounting";

        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static TestContainersService _instance;
        private bool _isDisposed;

        public bool IsReadyForUse { get; private set; }

        public static TestContainersService GetInstance { get; private set; } = _instance;
        public EventStoreDbContainer EventSourceDbContainer { get; private set; }
        public IContainer KafkaUIContainer { get; private set; }
        public KafkaContainer KafkaContainer { get; private set; }
        public IContainer ZkContainer { get; private set; }
        public INetwork KafkaNetwork { get; private set; }
        public MongoDbContainer MongoDbContainer { get; private set; }
        public MsSqlContainer MsSqlDbContainer { get; private set; }
        public string AccountingDbConnectionString => BuildAccountingDbConnectionString(MsSqlDbContainer.GetConnectionString());

        protected TestContainersService()
        {
        }

        public static async Task<TestContainersService> InitAsync()
        {
            if (GetInstance is not null)
            {
                return GetInstance;
            }

            await using (await SemaphoreGuard.WaitAsync(_lock))
            {
                if (_instance is null)
                {
                    _instance = new TestContainersService();

                    try
                    {
                        await _instance.UpAndRunningContainersAsync();
                        _instance.ApplyDbMigrations();
                    }
                    catch
                    {
                        await _instance.DisposeAsync();
                        _instance = null;
                        GetInstance = null;
                        throw;
                    }
                }

                GetInstance = _instance;

                return GetInstance;
            }
        }

        public async Task ResetContainersAsync()
        {
            await using (await SemaphoreGuard.WaitAsync(_lock))
            {
                try
                {
                    await MsSqlDbContainer.ResetContainersAsync();
                    await MongoDbContainer.ResetContainersAsync();
                    await KafkaContainer.ResetContainersAsync();
                    await EventSourceDbContainer.ResetContainersAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, ex.Message);
                    throw;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            if (EventSourceDbContainer != null)
            {
                await EventSourceDbContainer.DisposeAsync();
            }

            if (KafkaContainer != null)
            {
                await KafkaContainer.DisposeAsync();
            }

            if (KafkaUIContainer != null)
            {
                await KafkaUIContainer.DisposeAsync();
            }

            if (KafkaNetwork != null)
            {
                await KafkaNetwork.DisposeAsync();
            }

            if (MongoDbContainer != null)
            {
                await MongoDbContainer.DisposeAsync();
            }

            if (MsSqlDbContainer != null)
            {
                await MsSqlDbContainer.DisposeAsync();
            }

            _isDisposed = true;
        }

        private void ApplyDbMigrations()
        {
            var migrationsFolder = ResolveMigrationsFolder();
            var migrationFiles = Directory.GetFiles(migrationsFolder, "*.sql")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            EnsureExpectedMigrationsDiscovered(migrationsFolder, migrationFiles);
            EnsureIntegrationDatabasesExist();

            using var cnx = new SqlConnection(AccountingDbConnectionString);
            var evolve = new Evolve(cnx)
            {
                Locations = [migrationsFolder],
                EnableClusterMode = false,
                TransactionMode = TransactionKind.CommitEach
            };

            evolve.Migrate();
            ValidateOutboxTableExists(migrationsFolder, migrationFiles);
        }

        private void EnsureIntegrationDatabasesExist()
        {
            using var cnx = new SqlConnection(BuildMasterConnectionString(MsSqlDbContainer.GetConnectionString()));
            cnx.Open();

            ExecuteNonQuery(
                cnx,
                $@"
                IF NOT EXISTS(SELECT * FROM sys.databases WITH (NOLOCK) WHERE name = N'{HomeBudgetDatabaseName}')
                BEGIN
                    CREATE DATABASE [{HomeBudgetDatabaseName}];
                END;

                IF NOT EXISTS(SELECT * FROM sys.databases WITH (NOLOCK) WHERE name = N'{AccountingDatabaseName}')
                BEGIN
                    CREATE DATABASE [{AccountingDatabaseName}];
                END;");
        }

        private void ValidateOutboxTableExists(string migrationsFolder, IReadOnlyCollection<string> migrationFiles)
        {
            using var cnx = new SqlConnection(AccountingDbConnectionString);
            cnx.Open();

            var objectId = ExecuteScalar(cnx, "SELECT OBJECT_ID(N'dbo.OutboxAccountPayments', N'U');");

            if (objectId is not null)
            {
                return;
            }

            throw new InvalidOperationException(BuildOutboxValidationFailureMessage(cnx, migrationsFolder, migrationFiles));
        }

        private string BuildOutboxValidationFailureMessage(
            SqlConnection cnx,
            string migrationsFolder,
            IReadOnlyCollection<string> migrationFiles)
        {
            var message = new StringBuilder();
            message.AppendLine("Integration SQL migrations completed, but dbo.OutboxAccountPayments is missing.");
            message.AppendLine($"Current database: {ExecuteScalar(cnx, "SELECT DB_NAME();")}");
            message.AppendLine($"Connection string: {MaskPassword(AccountingDbConnectionString)}");
            message.AppendLine($"Migration folder: {migrationsFolder}");
            message.AppendLine("Migration files discovered:");
            message.AppendLine(string.Join(Environment.NewLine, migrationFiles.Select(file => $"  - {Path.GetFileName(file)}")));
            message.AppendLine("Migration history:");
            message.AppendLine(ReadMigrationHistory(cnx));
            message.AppendLine("dbo tables:");
            message.AppendLine(string.Join(
                Environment.NewLine,
                QueryStrings(cnx, "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') ORDER BY name;")
                    .Select(table => $"  - {table}")));

            return message.ToString();
        }

        private static string ResolveMigrationsFolder()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "db", "migrations"),
                Path.Combine(Directory.GetCurrentDirectory(), "db", "migrations"),
                Path.Combine(
                    FindRepositoryRoot(AppContext.BaseDirectory) ?? string.Empty,
                    "HomeBudget.Accounting.Api.IntegrationTests",
                    "db",
                    "migrations")
            };

            var migrationsFolder = candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.sql").Any());

            if (migrationsFolder is null)
            {
                throw new DirectoryNotFoundException(
                    $"Could not find integration SQL migrations. Checked: {string.Join(", ", candidates)}");
            }

            return migrationsFolder;
        }

        private static string FindRepositoryRoot(string startPath)
        {
            var directory = new DirectoryInfo(startPath);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HomeBudgetAccountingApi.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static void EnsureExpectedMigrationsDiscovered(string migrationsFolder, IReadOnlyCollection<string> migrationFiles)
        {
            var missingMigrations = Enumerable.Range(0, 6)
                .Select(version => $"V{version}__")
                .Where(prefix => migrationFiles.All(file =>
                    !Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (missingMigrations.Length == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"The integration SQL migration folder '{migrationsFolder}' is missing: {string.Join(", ", missingMigrations)}");
        }

        private static string BuildAccountingDbConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = AccountingDatabaseName
            };

            return builder.ConnectionString;
        }

        private static string BuildMasterConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master"
            };

            return builder.ConnectionString;
        }

        private static string MaskPassword(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);

            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***";
            }

            return builder.ConnectionString;
        }

        private static string ReadMigrationHistory(SqlConnection cnx)
        {
            var historyTables = QueryStrings(
                cnx,
                @"
                SELECT QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name)
                FROM sys.tables
                WHERE name IN (N'changelog', N'flyway_schema_history', N'schema_version')
                   OR name LIKE N'%changelog%'
                   OR name LIKE N'%schema%history%'
                ORDER BY name;");

            if (historyTables.Count == 0)
            {
                return "  <no migration history table found>";
            }

            var history = new StringBuilder();

            foreach (var historyTable in historyTables)
            {
                var json = ExecuteScalar(
                    cnx,
                    $"SELECT (SELECT TOP (50) * FROM {historyTable} ORDER BY 1 FOR JSON PATH);");

                history.AppendLine($"  {historyTable}: {json ?? "[]"}");
            }

            return history.ToString();
        }

        private static IReadOnlyCollection<string> QueryStrings(SqlConnection cnx, string sql)
        {
            using var command = cnx.CreateCommand();
            command.CommandText = sql;

            using var reader = command.ExecuteReader();
            var results = new List<string>();

            while (reader.Read())
            {
                results.Add(reader.IsDBNull(0) ? "<null>" : reader.GetValue(0).ToString());
            }

            return results;
        }

        private static object ExecuteScalar(SqlConnection cnx, string sql)
        {
            using var command = cnx.CreateCommand();
            command.CommandText = sql;

            var result = command.ExecuteScalar();

            return result == DBNull.Value ? null : result;
        }

        private static void ExecuteNonQuery(SqlConnection cnx, string sql)
        {
            using var command = cnx.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private async Task<bool> UpAndRunningContainersAsync()
        {
            if (IsReadyForUse)
            {
                return true;
            }

            try
            {
                EventSourceDbContainer = EventStoreDbContainerFactory.Build();

                var networkName = $"test-kafka-net-{Guid.NewGuid().ToString("N").Substring(0, 6)}";

                // TODO: Use Zookeeper instead of KRaft (More Stable for CI)
                KafkaNetwork = await DockerNetworkFactory.GetOrCreateDockerNetworkAsync(networkName);
                KafkaContainer = KafkaContainerFactory.BuildWithZkMode(KafkaNetwork);
                ZkContainer = await ZookeperKafkaContainerFactory.BuildAsync(KafkaNetwork);
                KafkaUIContainer = await KafkaUIContainerFactory.BuildAsync(KafkaNetwork);

                MongoDbContainer = MongoDbContainerFactory.Build();
                MsSqlDbContainer = MsSqlContainerFactory.Build();

                await TryToStartContainerAsync();

                IsReadyForUse = true;

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                IsReadyForUse = false;

                throw;
            }
        }

        private async Task<bool> TryToStartContainerAsync()
        {
            try
            {
                if (EventSourceDbContainer is not null)
                {
                    await EventSourceDbContainer.SafeStartWithRetryAsync();
                }

                if (MongoDbContainer is not null)
                {
                    await MongoDbContainer.SafeStartWithRetryAsync();
                }

                if (MsSqlDbContainer is not null)
                {
                    await MsSqlDbContainer.SafeStartWithRetryAsync();
                }

                if (ZkContainer is not null)
                {
                    await ZkContainer.SafeStartWithRetryAsync();
                }

                if (KafkaContainer is not null)
                {
                    await KafkaContainer.SafeStartWithRetryAsync(swallowBusyError: true);
                    await KafkaContainer.WaitForKafkaReadyAsync(TimeSpan.FromMinutes(BaseTestContainerOptions.StopTimeoutInMinutes));
                }

                if (KafkaUIContainer is not null)
                {
                    await KafkaUIContainer.SafeStartWithRetryAsync();
                }

                Log.Information($"The topics have been created: {BaseTopics.AccountingAccounts}, {BaseTopics.AccountingPayments}");
            }
            catch (Exception ex)
            {
                Log.Information("Container startup failed:");
                Log.Error(ex, ex.Message);

                await MsSqlDbContainer.DumpContainerLogsSafelyAsync("MsSqlDB");
                await MongoDbContainer.DumpContainerLogsSafelyAsync("MongoDB");
                await EventSourceDbContainer.DumpContainerLogsSafelyAsync("EventStoreDB");
                await KafkaContainer.DumpContainerLogsSafelyAsync("Kafka");

                throw;
            }

            return true;
        }
    }
}
