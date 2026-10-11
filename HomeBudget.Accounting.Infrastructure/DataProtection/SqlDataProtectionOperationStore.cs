using System;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed partial class SqlDataProtectionOperationStore : IDataProtectionOperationStore
    {
        private const string SelectColumns = @"
            OperationId, OperationKind, ScopeKind, ScopeKey, EnvironmentId, LedgerId,
            GenerationId, PlanFingerprint, State AS StateValue, Stage, CancellationRequested,
            CreatedUtc, UpdatedUtc, RowVersion";

        private readonly string _connectionString;

        public SqlDataProtectionOperationStore(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            };
            _connectionString = builder.ConnectionString;
        }

        public async Task<DataProtectionOperation> EnqueueAsync(
            DataProtectionOperationRequest request,
            CancellationToken cancellationToken)
        {
            ValidateRequest(request);
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                INSERT INTO dbo.DataProtectionOperations
                    (OperationId, OperationKind, ScopeKind, ScopeKey, EnvironmentId, LedgerId,
                     GenerationId, PlanFingerprint, State, CreatedUtc, UpdatedUtc)
                VALUES
                    (@OperationId, @OperationKind, @ScopeKind, @ScopeKey, @EnvironmentId, @LedgerId,
                     @GenerationId, @PlanFingerprint, N'queued', @CreatedUtc, @CreatedUtc);

                INSERT INTO dbo.ProtectedAuditEntries
                    (AuditId, OperationId, ActorId, EventType, ScopeKey, TargetEnvironmentId,
                     TargetLedgerId, TargetGenerationId, PlanFingerprint, Result, OccurredUtc)
                VALUES
                    (NEWID(), @OperationId, @ActorId, N'operation-created', @ScopeKey, @EnvironmentId,
                     @LedgerId, @GenerationId, @PlanFingerprint, N'queued', @CreatedUtc);

                COMMIT TRANSACTION;

                SELECT " + SelectColumns + @"
                FROM dbo.DataProtectionOperations
                WHERE OperationId = @OperationId;";

            await using var connection = CreateConnection();
            return await connection.QuerySingleAsync<DataProtectionOperation>(
                new CommandDefinition(sql, request, cancellationToken: cancellationToken));
        }

        public async Task<DataProtectionOperation> GetAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            var sql = "SELECT " + SelectColumns + @"
                FROM dbo.DataProtectionOperations
                WHERE OperationId = @OperationId;";
            await using var connection = CreateConnection();
            return await connection.QuerySingleOrDefaultAsync<DataProtectionOperation>(
                new CommandDefinition(sql, new { OperationId = operationId }, cancellationToken: cancellationToken));
        }

        public async Task<DataProtectionOperation> GetNextRunnableAsync(CancellationToken cancellationToken)
        {
            var sql = "SELECT TOP (1) " + SelectColumns + @"
                FROM dbo.DataProtectionOperations AS operation WITH (READPAST)
                WHERE State IN (N'queued', N'validating', N'running', N'awaiting-reconciliation')
                  AND NOT EXISTS
                  (
                      SELECT 1
                        FROM dbo.DataProtectionLeases AS lease
                       WHERE lease.ScopeKey = operation.ScopeKey
                         AND lease.ExpiresUtc > SYSUTCDATETIME()
                  )
                ORDER BY CreatedUtc, OperationId;";
            await using var connection = CreateConnection();
            return await connection.QuerySingleOrDefaultAsync<DataProtectionOperation>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
        }

        public async Task<bool> TryTransitionAsync(
            DataProtectionOperationFenceContext fenceContext,
            DataProtectionOperationState expectedState,
            DataProtectionOperationState nextState,
            string actorId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            ValidateFenceContext(fenceContext);
            DataProtectionOperationTransitions.EnsureAllowed(expectedState, nextState);
            ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                UPDATE dbo.DataProtectionOperations
                   SET State = @NextState, UpdatedUtc = @NowUtc
                 WHERE OperationId = @OperationId
                   AND ScopeKey = @ScopeKey
                   AND State = @ExpectedState
                   AND EXISTS
                   (
                       SELECT 1
                         FROM dbo.DataProtectionLeases
                        WHERE LeaseId = @LeaseId
                          AND OperationId = @OperationId
                          AND ScopeKey = @ScopeKey
                          AND OwnerId = @OwnerId
                          AND FenceToken = @FenceToken
                          AND ExpiresUtc > @NowUtc
                   );
                DECLARE @Changed int = @@ROWCOUNT;

                IF @Changed = 1
                BEGIN
                    INSERT INTO dbo.ProtectedAuditEntries
                        (AuditId, OperationId, ActorId, EventType, ScopeKey, TargetEnvironmentId,
                         TargetLedgerId, TargetGenerationId, PlanFingerprint, Result, OccurredUtc)
                    SELECT NEWID(), OperationId, @ActorId, N'state-transition', ScopeKey, EnvironmentId,
                           LedgerId, GenerationId, PlanFingerprint, @NextState, @NowUtc
                      FROM dbo.DataProtectionOperations
                     WHERE OperationId = @OperationId;
                END;

                COMMIT TRANSACTION;
                SELECT @Changed;";
            var parameters = new
            {
                fenceContext.OperationId,
                fenceContext.LeaseId,
                fenceContext.ScopeKey,
                fenceContext.OwnerId,
                fenceContext.FenceToken,
                ExpectedState = DataProtectionOperationStateNames.Format(expectedState),
                NextState = DataProtectionOperationStateNames.Format(nextState),
                ActorId = actorId,
                NowUtc = nowUtc
            };
            await using var connection = CreateConnection();
            var changed = await connection.QuerySingleAsync<int>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
            return changed == 1;
        }

        public async Task<bool> RequestCancellationAsync(
            Guid operationId,
            string actorId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                UPDATE dbo.DataProtectionOperations
                   SET CancellationRequested = 1, UpdatedUtc = @NowUtc
                 WHERE OperationId = @OperationId
                   AND CancellationRequested = 0
                   AND State NOT IN (N'succeeded', N'failed', N'cancelled');
                DECLARE @Changed int = @@ROWCOUNT;

                IF @Changed = 1
                BEGIN
                    INSERT INTO dbo.ProtectedAuditEntries
                        (AuditId, OperationId, ActorId, EventType, ScopeKey, TargetEnvironmentId,
                         TargetLedgerId, TargetGenerationId, PlanFingerprint, Result, OccurredUtc)
                    SELECT NEWID(), OperationId, @ActorId, N'cancellation-requested', ScopeKey, EnvironmentId,
                           LedgerId, GenerationId, PlanFingerprint, N'requested', @NowUtc
                      FROM dbo.DataProtectionOperations
                     WHERE OperationId = @OperationId;
                END;

                COMMIT TRANSACTION;
                SELECT @Changed;";
            await using var connection = CreateConnection();
            var changed = await connection.QuerySingleAsync<int>(new CommandDefinition(
                sql,
                new { OperationId = operationId, ActorId = actorId, NowUtc = nowUtc },
                cancellationToken: cancellationToken));
            return changed == 1;
        }

        private SqlConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }

        private static void ValidateRequest(DataProtectionOperationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.OperationId == Guid.Empty)
            {
                throw new ArgumentException("Operation ID is required.", nameof(request));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationKind);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ScopeKind);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ScopeKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.EnvironmentId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.PlanFingerprint);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId);
        }

        private static void ValidateFenceContext(DataProtectionOperationFenceContext fenceContext)
        {
            ArgumentNullException.ThrowIfNull(fenceContext);
            if (fenceContext.OperationId == Guid.Empty || fenceContext.LeaseId == Guid.Empty)
            {
                throw new ArgumentException("Operation and lease IDs are required.", nameof(fenceContext));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(fenceContext.ScopeKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(fenceContext.OwnerId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fenceContext.FenceToken);
        }
    }
}
