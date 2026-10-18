using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed partial class SqlCaptureCoordinationStore :
        ICaptureCoordinationStore,
        IMaintenanceAdmissionFence
    {
        private readonly string _connectionString;

        public SqlCaptureCoordinationStore(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            _connectionString = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            }.ConnectionString;
        }

        public async Task<MaintenanceEpoch> CloseAdmissionAsync(
            CaptureRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var epochId = Guid.NewGuid();
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DECLARE @LockResult INT;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @GlobalLockResource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 30000;
                IF @LockResult < 0
                    THROW 51020, 'Unable to acquire the global maintenance admission fence.', 1;

                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @LockResource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 30000;
                IF @LockResult < 0
                    THROW 51021, 'Unable to acquire maintenance admission fence.', 1;

                INSERT INTO dbo.MaintenanceEpochs
                    (EpochId, OperationId, ScopeKey, GenerationId, State, AdmissionClosedUtc)
                SELECT @EpochId, operation.OperationId, operation.ScopeKey, operation.GenerationId,
                       N'closed', SYSUTCDATETIME()
                  FROM dbo.DataProtectionOperations AS operation
                 WHERE operation.OperationId = @OperationId
                   AND operation.ScopeKey = @ScopeKey
                   AND operation.GenerationId = @GenerationId
                   AND EXISTS
                   (
                       SELECT 1
                         FROM dbo.DataProtectionLeases AS lease
                        WHERE lease.LeaseId = @LeaseId
                          AND lease.OperationId = @OperationId
                          AND lease.ScopeKey = @ScopeKey
                          AND lease.OwnerId = @OwnerId
                          AND lease.FenceToken = @FenceToken
                          AND lease.ExpiresUtc > SYSUTCDATETIME()
                   );
                IF @@ROWCOUNT <> 1
                    THROW 51022, 'Operation lease no longer owns the maintenance scope.', 1;

                COMMIT TRANSACTION;
                SELECT EpochId, OperationId, ScopeKey, GenerationId, EpochNumber, AdmissionClosedUtc
                  FROM dbo.MaintenanceEpochs
                 WHERE EpochId = @EpochId;";
            var fence = request.FenceContext;
            var parameters = new
            {
                EpochId = epochId,
                request.Operation.OperationId,
                request.Operation.ScopeKey,
                GenerationId = request.Operation.GenerationId,
                fence.LeaseId,
                fence.OwnerId,
                fence.FenceToken,
                GlobalLockResource = "HomeBudget.DataProtection.Admission.Global.v1",
                LockResource = LockResource("admission", request.Operation.ScopeKey)
            };

            await using var connection = CreateConnection();
            var row = await connection.QuerySingleAsync<MaintenanceEpochRow>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
            return row.ToEpoch();
        }

        public async Task<IMaintenanceAdmissionPermit> AcquireAdmissionPermitAsync(
            string scopeKey,
            string generationId,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(generationId);
            const string sql = @"
                DECLARE @LockResult INT;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @LockResource,
                    @LockMode = 'Shared',
                    @LockOwner = 'Session',
                    @LockTimeout = 30000;
                IF @LockResult < 0
                    THROW 51023, 'Unable to acquire the maintenance admission permit.', 1;

                SELECT CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.MaintenanceEpochs
                     WHERE ScopeKey = @ScopeKey
                       AND GenerationId = @GenerationId
                       AND State = N'closed'
                ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
            var connection = CreateConnection();
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var closed = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                    sql,
                    new
                    {
                        ScopeKey = scopeKey,
                        GenerationId = generationId,
                        LockResource = LockResource("admission", scopeKey)
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (closed)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw new MaintenanceAdmissionClosedException();
                }

                return new SqlMaintenanceAdmissionPermit(
                    connection,
                    LockResource("admission", scopeKey));
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<bool> IsAdmissionClosedAsync(
            string scopeKey,
            string generationId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.MaintenanceEpochs
                     WHERE ScopeKey = @ScopeKey
                       AND GenerationId = @GenerationId
                       AND State = N'closed'
                ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
            await using var connection = CreateConnection();
            return await connection.QuerySingleAsync<bool>(
                new CommandDefinition(
                    sql,
                    new { ScopeKey = scopeKey, GenerationId = generationId },
                    cancellationToken: cancellationToken));
        }

        public async Task<DateTimeOffset?> GetProtectedCutoffAsync(
            string environmentId,
            string ledgerScope,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT MAX(CommonCutoffUtc)
                  FROM dbo.RecoveryManifests
                 WHERE EnvironmentId = @EnvironmentId
                   AND LedgerScope = @LedgerScope
                   AND VerificationStatus = N'verified'
                   AND ProtectedUtc IS NOT NULL
                   AND PublishedUtc IS NOT NULL;";
            await using var connection = CreateConnection();
            var value = await connection.QuerySingleAsync<DateTime?>(
                new CommandDefinition(
                    sql,
                    new { EnvironmentId = environmentId, LedgerScope = ledgerScope },
                    cancellationToken: cancellationToken));
            return value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null;
        }

        public async Task ReleaseEpochAsync(
            MaintenanceEpoch epoch,
            bool failed,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                UPDATE dbo.MaintenanceEpochs
                   SET State = @State, ReleasedUtc = SYSUTCDATETIME()
                 WHERE EpochId = @EpochId
                   AND OperationId = @OperationId
                   AND ScopeKey = @ScopeKey
                   AND GenerationId = @GenerationId
                   AND EpochNumber = @EpochNumber
                   AND State = N'closed';
                SELECT @@ROWCOUNT;";
            await using var connection = CreateConnection();
            var changed = await connection.QuerySingleAsync<int>(new CommandDefinition(
                sql,
                new
                {
                    epoch.EpochId,
                    epoch.OperationId,
                    epoch.ScopeKey,
                    epoch.GenerationId,
                    epoch.EpochNumber,
                    State = failed ? "failed" : "released"
                },
                cancellationToken: cancellationToken));
            if (changed != 1)
            {
                throw new InvalidOperationException("Maintenance epoch is no longer active.");
            }
        }

        private SqlConnection CreateConnection() => new(_connectionString);

        private static string LockResource(string kind, string identity)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            return $"HomeBudget.DataProtection.{kind}.{hash}";
        }

        private sealed class MaintenanceEpochRow
        {
            public Guid EpochId { get; set; }
            public Guid OperationId { get; set; }
            public string ScopeKey { get; set; }
            public string GenerationId { get; set; }
            public long EpochNumber { get; set; }
            public DateTime AdmissionClosedUtc { get; set; }

            public MaintenanceEpoch ToEpoch() => new(
                EpochId,
                OperationId,
                ScopeKey,
                GenerationId,
                EpochNumber,
                new DateTimeOffset(DateTime.SpecifyKind(AdmissionClosedUtc, DateTimeKind.Utc)));
        }
    }
}
