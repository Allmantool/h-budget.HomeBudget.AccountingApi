using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public sealed class SqlServerProtectionProvider : ISqlServerProtectionProvider
    {
        private readonly string _connectionString;
        private readonly SqlServerProtectionPolicy _policy;

        public SqlServerProtectionProvider(
            string connectionString,
            SqlServerProtectionPolicy policy)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master",
                PersistSecurityInfo = false
            };
            _connectionString = builder.ConnectionString;
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public async Task<SqlServerProtectionProbe> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var connection = await OpenConnectionAsync(cancellationToken);
                return await ReadProbeAsync(connection, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SqlException exception)
            {
                throw SafeFailure("SQL Server protection probe failed for the allowlisted database.", exception);
            }
        }

        public async Task<SqlServerCapturePlan> PlanCaptureAsync(
            SqlServerBackupKind kind,
            Guid planId,
            CancellationToken cancellationToken)
        {
            if (planId == Guid.Empty)
            {
                throw new ArgumentException("A non-empty capture plan ID is required.", nameof(planId));
            }

            var probe = await ProbeAsync(cancellationToken);
            EnsureCompatible(probe);
            return new SqlServerCapturePlan(
                planId,
                _policy.DatabaseName,
                kind,
                BuildBackupTarget(kind, planId),
                probe.ServerName,
                probe.ProductVersion,
                probe.DatabaseId,
                DateTimeOffset.UtcNow);
        }

        public async Task<SqlServerCaptureResult> ExecuteCaptureAsync(
            SqlServerCapturePlan plan,
            CancellationToken cancellationToken)
        {
            ValidatePlan(plan);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var connection = await OpenConnectionAsync(cancellationToken);
                var probe = await ReadProbeAsync(connection, cancellationToken);
                EnsureCompatible(probe);
                EnsureSameTarget(plan, probe);
                await ExecuteBackupAsync(connection, plan, cancellationToken);
                await VerifyBackupAsync(connection, plan.BackupTarget, cancellationToken);
                var metadata = await ReadMetadataAsync(connection, plan, cancellationToken);
                EnsureMetadataMatchesPlan(plan, metadata);
                return new SqlServerCaptureResult(plan, metadata with { IsVerified = true });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SqlException exception)
            {
                throw SafeFailure("SQL Server backup capture or verification failed for the allowlisted target.", exception);
            }
        }

        public void ValidateChain(IReadOnlyCollection<SqlServerBackupMetadata> backups)
        {
            SqlServerBackupChainPlanner.Validate(backups);
        }

        public SqlServerPointInTimeRecoveryPlan PlanPointInTimeRecovery(
            DateTimeOffset requestedCutoffUtc,
            IReadOnlyCollection<SqlServerBackupMetadata> backups)
        {
            return SqlServerBackupChainPlanner.Plan(requestedCutoffUtc, backups);
        }

        private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            var connection = new SqlConnection(_connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        private async Task<SqlServerProtectionProbe> ReadProbeAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = _policy.CommandTimeoutSeconds;
            command.CommandText = @"
                SELECT
                    CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS ServerName,
                    CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS ProductVersion,
                    CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) AS ProductMajorVersion,
                    CONVERT(nvarchar(128), SERVERPROPERTY('ProductLevel')) AS ProductLevel,
                    CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) AS Edition,
                    CONVERT(int, SERVERPROPERTY('EngineEdition')) AS EngineEdition,
                    d.name,
                    drs.database_guid,
                    d.recovery_model_desc,
                    d.state_desc,
                    SYSDATETIMEOFFSET()
                FROM sys.databases d
                INNER JOIN sys.database_recovery_status drs ON drs.database_id = d.database_id
                WHERE d.name = @databaseName;";
            command.Parameters.Add("@databaseName", SqlDbType.NVarChar, 128).Value = _policy.DatabaseName;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new SqlServerProtectionException("The allowlisted SQL Server database does not exist.");
            }

            var probe = new SqlServerProtectionProbe(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetGuid(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetDateTimeOffset(10).ToUniversalTime(),
                Array.Empty<string>());
            return probe with { Blockers = FindBlockers(probe) };
        }

        private IReadOnlyCollection<string> FindBlockers(SqlServerProtectionProbe probe)
        {
            var blockers = new List<string>();
            AddMismatch(blockers, probe.ServerName, _policy.ExpectedServerName, "server-name-mismatch");
            AddMismatch(blockers, probe.ProductVersion, _policy.ExpectedProductVersion, "exact-product-version-mismatch");
            AddMismatch(blockers, probe.Edition, _policy.ExpectedEdition, "edition-mismatch");
            AddMismatch(blockers, probe.RecoveryModel, _policy.ExpectedRecoveryModel, "recovery-model-mismatch");
            AddMismatch(blockers, probe.DatabaseState, "ONLINE", "database-not-online");
            if (probe.ProductMajorVersion != 16)
            {
                blockers.Add("unsupported-product-major-version");
            }

            return blockers;
        }

        private static void AddMismatch(
            ICollection<string> blockers,
            string actual,
            string expected,
            string blocker)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                blockers.Add(blocker);
            }
        }

        private static void EnsureCompatible(SqlServerProtectionProbe probe)
        {
            if (!probe.IsCompatible)
            {
                throw new SqlServerProtectionException(
                    $"SQL Server protection capability is blocked: {string.Join(", ", probe.Blockers)}.");
            }
        }

        private void EnsureSameTarget(SqlServerCapturePlan plan, SqlServerProtectionProbe probe)
        {
            if (!string.Equals(plan.ServerName, probe.ServerName, StringComparison.Ordinal) ||
                !string.Equals(plan.ProductVersion, probe.ProductVersion, StringComparison.Ordinal) ||
                plan.DatabaseId != probe.DatabaseId)
            {
                throw new SqlServerProtectionException("The SQL Server target identity changed after capture planning.");
            }
        }

        private string BuildBackupTarget(SqlServerBackupKind kind, Guid planId)
        {
            var extension = kind switch
            {
                SqlServerBackupKind.Full => "bak",
                SqlServerBackupKind.Differential => "dif",
                SqlServerBackupKind.Log => "trn",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{_policy.BackupRoot}/home-ledger-{planId:N}.{extension}");
        }

        private void ValidatePlan(SqlServerCapturePlan plan)
        {
            if (plan is null || plan.PlanId == Guid.Empty ||
                !string.Equals(plan.DatabaseName, _policy.DatabaseName, StringComparison.Ordinal) ||
                !string.Equals(plan.BackupTarget, BuildBackupTarget(plan.Kind, plan.PlanId), StringComparison.Ordinal) ||
                !plan.BackupTarget.StartsWith(_policy.BackupRoot + "/", StringComparison.Ordinal))
            {
                throw new SqlServerProtectionException("The SQL Server capture plan is stale or outside the allowlist.");
            }
        }

        private async Task ExecuteBackupAsync(
            SqlConnection connection,
            SqlServerCapturePlan plan,
            CancellationToken cancellationToken)
        {
            var identifier = $"[{_policy.DatabaseName.Replace("]", "]]", StringComparison.Ordinal)}]";
            var statement = plan.Kind switch
            {
                SqlServerBackupKind.Full => $"BACKUP DATABASE {identifier} TO DISK = @target WITH INIT, CHECKSUM;",
                SqlServerBackupKind.Differential => $"BACKUP DATABASE {identifier} TO DISK = @target WITH DIFFERENTIAL, INIT, CHECKSUM;",
                SqlServerBackupKind.Log => $"BACKUP LOG {identifier} TO DISK = @target WITH INIT, CHECKSUM;",
                _ => throw new ArgumentOutOfRangeException(nameof(plan))
            };
            await ExecuteTargetCommandAsync(connection, statement, plan.BackupTarget, cancellationToken);
        }

        private async Task VerifyBackupAsync(
            SqlConnection connection,
            string backupTarget,
            CancellationToken cancellationToken)
        {
            await ExecuteTargetCommandAsync(
                connection,
                "RESTORE VERIFYONLY FROM DISK = @target WITH CHECKSUM;",
                backupTarget,
                cancellationToken);
        }

        [SuppressMessage(
            "Security",
            "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "Only caller-owned constant statements or an allowlisted and quoted database identifier reach this method; the target is parameterized.")]
        private async Task ExecuteTargetCommandAsync(
            SqlConnection connection,
            string statement,
            string target,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = _policy.CommandTimeoutSeconds;
            command.CommandText = statement;
            command.Parameters.Add("@target", SqlDbType.NVarChar, 4000).Value = target;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<SqlServerBackupMetadata> ReadMetadataAsync(
            SqlConnection connection,
            SqlServerCapturePlan plan,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = _policy.CommandTimeoutSeconds;
            command.CommandText = MetadataQuery;
            command.Parameters.Add("@databaseName", SqlDbType.NVarChar, 128).Value = _policy.DatabaseName;
            command.Parameters.Add("@target", SqlDbType.NVarChar, 4000).Value = plan.BackupTarget;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new SqlServerProtectionException("SQL Server did not record backupset metadata for the capture.");
            }

            var offset = TimeSpan.FromMinutes(reader.GetInt32(22));
            return new SqlServerBackupMetadata
            {
                BackupSetId = reader.GetInt32(0),
                BackupSetUuid = reader.GetGuid(1),
                DatabaseName = reader.GetString(2),
                Kind = ParseKind(reader.GetString(3)),
                FirstLsn = reader.GetDecimal(4),
                LastLsn = reader.GetDecimal(5),
                CheckpointLsn = reader.GetDecimal(6),
                DatabaseBackupLsn = reader.GetDecimal(7),
                DifferentialBaseLsn = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                RecoveryForkId = reader.GetGuid(9),
                BackupStartUtc = ToUtc(reader.GetDateTime(10), offset),
                BackupFinishUtc = ToUtc(reader.GetDateTime(11), offset),
                LastValidRestoreTimeUtc = reader.IsDBNull(12) ? null : ToUtc(reader.GetDateTime(12), offset),
                HasBackupChecksums = reader.GetBoolean(13),
                IsDamaged = reader.GetBoolean(14),
                HasIncompleteMetadata = reader.GetBoolean(15),
                BeginsLogChain = reader.GetBoolean(16),
                BackupSizeBytes = checked((long)reader.GetDecimal(17)),
                CompressedBackupSizeBytes = checked((long)reader.GetDecimal(18)),
                RecoveryModel = reader.GetString(19),
                PhysicalDeviceName = reader.GetString(20)
            };
        }

        private static void EnsureMetadataMatchesPlan(
            SqlServerCapturePlan plan,
            SqlServerBackupMetadata metadata)
        {
            if (metadata.Kind != plan.Kind ||
                !string.Equals(metadata.DatabaseName, plan.DatabaseName, StringComparison.Ordinal) ||
                !string.Equals(metadata.PhysicalDeviceName, plan.BackupTarget, StringComparison.Ordinal) ||
                !metadata.HasBackupChecksums || metadata.IsDamaged || metadata.HasIncompleteMetadata)
            {
                throw new SqlServerProtectionException("SQL Server backupset metadata failed capture verification.");
            }
        }

        private static SqlServerBackupKind ParseKind(string value)
        {
            return value switch
            {
                "D" => SqlServerBackupKind.Full,
                "I" => SqlServerBackupKind.Differential,
                "L" => SqlServerBackupKind.Log,
                _ => throw new SqlServerProtectionException("SQL Server returned an unsupported backupset type.")
            };
        }

        private static DateTimeOffset ToUtc(DateTime value, TimeSpan serverOffset)
        {
            var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, serverOffset).ToUniversalTime();
        }

        private static SqlServerProtectionException SafeFailure(string message, SqlException exception)
        {
            var knownMetadataColumns = new[]
            {
                "backup_set_id",
                "backup_set_uuid",
                "database_name",
                "first_lsn",
                "last_lsn",
                "checkpoint_lsn",
                "database_backup_lsn",
                "differential_base_lsn",
                "last_recovery_fork_guid",
                "backup_start_date",
                "backup_finish_date",
                "last_valid_restore_time",
                "has_backup_checksums",
                "is_damaged",
                "has_incomplete_metadata",
                "begins_log_chain",
                "backup_size",
                "compressed_backup_size",
                "recovery_model",
                "physical_device_name",
                "media_set_id"
            };
            var unavailableColumn = exception.Number == 207
                ? knownMetadataColumns.FirstOrDefault(column =>
                    exception.Message.Contains(column, StringComparison.OrdinalIgnoreCase))
                : null;
            var safeDiagnostic = unavailableColumn is null
                ? string.Empty
                : $" Required SQL Server backupset metadata is unavailable: {unavailableColumn}.";
            return new SqlServerProtectionException(message + safeDiagnostic, exception.Number);
        }

        private const string MetadataQuery = @"
            SELECT TOP (1)
                bs.backup_set_id,
                bs.backup_set_uuid,
                bs.database_name,
                bs.type,
                bs.first_lsn,
                bs.last_lsn,
                bs.checkpoint_lsn,
                bs.database_backup_lsn,
                bs.differential_base_lsn,
                bs.last_recovery_fork_guid,
                bs.backup_start_date,
                bs.backup_finish_date,
                bs.last_valid_restore_time,
                bs.has_backup_checksums,
                bs.is_damaged,
                bs.has_incomplete_metadata,
                bs.begins_log_chain,
                bs.backup_size,
                bs.compressed_backup_size,
                bs.recovery_model,
                bmf.physical_device_name,
                bs.position,
                DATEPART(TZOFFSET, SYSDATETIMEOFFSET())
            FROM msdb.dbo.backupset bs
            INNER JOIN msdb.dbo.backupmediafamily bmf ON bmf.media_set_id = bs.media_set_id
            WHERE bs.database_name = @databaseName
              AND bmf.physical_device_name = @target
            ORDER BY bs.backup_set_id DESC;";
    }
}
