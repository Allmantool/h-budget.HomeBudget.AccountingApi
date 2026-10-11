using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Triggers
{
    internal sealed class SqlRecoveryTriggerStateStore : IRecoveryTriggerStateStore
    {
        private const string DatabaseName = "HomeBudget.DataProtection";
        private readonly string _connectionString;

        public SqlRecoveryTriggerStateStore(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            _connectionString = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = DatabaseName
            }.ConnectionString;
        }

        public async Task<RecoveryTriggerState> GetAsync(
            RecoveryTriggerTarget target,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            const string sql = @"
                SELECT PendingRecordCount, PendingByteCount, FirstPendingUtc, LastChangeUtc,
                       LastScheduledUtc, LastTriggeredUtc, LastTriggerReason, RowVersion
                  FROM dbo.RecoveryTriggerStates
                 WHERE EnvironmentId = @EnvironmentId
                   AND ScopeKey = @ScopeKey
                   AND GenerationId = @GenerationId
                   AND LedgerId = @LedgerId;";
            await using var connection = new SqlConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<TriggerStateRow>(new CommandDefinition(
                sql,
                target,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return row?.ToState(target);
        }

        public async Task<bool> TryCommitAsync(
            RecoveryTriggerCommit commit,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(commit);
            ArgumentNullException.ThrowIfNull(commit.NextState);
            var state = commit.NextState;
            var target = state.Target;
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DECLARE @LockResult INT;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @LockResource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 30000;
                IF @LockResult < 0
                    THROW 51040, 'Unable to acquire recovery trigger state lock.', 1;

                DECLARE @Changed INT = 0;
                IF @IsNew = 1
                BEGIN
                    IF NOT EXISTS
                    (
                        SELECT 1 FROM dbo.RecoveryTriggerStates
                         WHERE EnvironmentId = @EnvironmentId
                           AND ScopeKey = @ScopeKey
                           AND GenerationId = @GenerationId
                    )
                    BEGIN
                        INSERT INTO dbo.RecoveryTriggerStates
                            (EnvironmentId, LedgerId, GenerationId, ScopeKey, PendingRecordCount,
                             PendingByteCount, FirstPendingUtc, LastChangeUtc, LastScheduledUtc,
                             LastTriggeredUtc, LastTriggerReason)
                        VALUES
                            (@EnvironmentId, @LedgerId, @GenerationId, @ScopeKey, @PendingRecordCount,
                             @PendingByteCount, @FirstPendingUtc, @LastChangeUtc, @LastScheduledUtc,
                             @LastTriggeredUtc, @LastTriggerReason);
                        SET @Changed = 1;
                    END;
                END;
                ELSE
                BEGIN
                    UPDATE dbo.RecoveryTriggerStates
                       SET PendingRecordCount = @PendingRecordCount,
                           PendingByteCount = @PendingByteCount,
                           FirstPendingUtc = @FirstPendingUtc,
                           LastChangeUtc = @LastChangeUtc,
                           LastScheduledUtc = @LastScheduledUtc,
                           LastTriggeredUtc = @LastTriggeredUtc,
                           LastTriggerReason = @LastTriggerReason
                     WHERE EnvironmentId = @EnvironmentId
                       AND ScopeKey = @ScopeKey
                       AND GenerationId = @GenerationId
                       AND LedgerId = @LedgerId
                       AND RowVersion = @ExpectedConcurrencyToken;
                    SET @Changed = @@ROWCOUNT;
                END;

                IF @Changed <> 1
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 0;
                    RETURN;
                END;

                IF @HasDispatch = 1
                BEGIN
                    INSERT INTO dbo.DataProtectionOperations
                        (OperationId, OperationKind, ScopeKind, ScopeKey, EnvironmentId, LedgerId,
                         GenerationId, PlanFingerprint, State, CreatedUtc, UpdatedUtc)
                    VALUES
                        (@OperationId, N'backup', N'ledger', @ScopeKey, @EnvironmentId, @LedgerId,
                         @GenerationId, @PlanFingerprint, N'queued', @DispatchCreatedUtc, @DispatchCreatedUtc);

                    INSERT INTO dbo.RecoveryTriggerDispatches
                        (OperationId, TriggerKind, TriggerReason, SourceIdentity, RecordCount,
                         ByteCount, KeyReference, RequiredComponentsJson, CreatedUtc)
                    VALUES
                        (@OperationId, @TriggerKind, @TriggerReason, @SourceIdentity, @DispatchRecordCount,
                         @DispatchByteCount, @KeyReference, @RequiredComponentsJson, @DispatchCreatedUtc);

                    INSERT INTO dbo.ProtectedAuditEntries
                        (AuditId, OperationId, ActorId, EventType, ScopeKey, TargetEnvironmentId,
                         TargetLedgerId, TargetGenerationId, PlanFingerprint, Result, OccurredUtc)
                    VALUES
                        (NEWID(), @OperationId, @ActorId, N'recovery-triggered', @ScopeKey, @EnvironmentId,
                         @LedgerId, @GenerationId, @PlanFingerprint, @TriggerKind, @DispatchCreatedUtc);
                END;

                COMMIT TRANSACTION;
                SELECT 1;";
            var dispatch = commit.Dispatch;
            var parameters = new
            {
                target.EnvironmentId,
                target.LedgerId,
                target.GenerationId,
                target.ScopeKey,
                state.PendingRecordCount,
                state.PendingByteCount,
                FirstPendingUtc = Utc(state.FirstPendingUtc),
                LastChangeUtc = Utc(state.LastChangeUtc),
                LastScheduledUtc = Utc(state.LastScheduledUtc),
                LastTriggeredUtc = Utc(state.LastTriggeredUtc),
                state.LastTriggerReason,
                IsNew = commit.ExpectedConcurrencyToken is null,
                ExpectedConcurrencyToken = commit.ExpectedConcurrencyToken,
                LockResource = LockResource(target),
                HasDispatch = dispatch is not null,
                OperationId = dispatch?.OperationId,
                PlanFingerprint = dispatch?.PlanFingerprint,
                TriggerKind = dispatch is null ? null : Format(dispatch.TriggerKind),
                dispatch?.TriggerReason,
                dispatch?.SourceIdentity,
                DispatchRecordCount = dispatch?.RecordCount,
                DispatchByteCount = dispatch?.ByteCount,
                target.KeyReference,
                RequiredComponentsJson = JsonSerializer.Serialize(
                    target.RequiredComponents.OrderBy(value => value, StringComparer.Ordinal)),
                DispatchCreatedUtc = dispatch?.CreatedUtc.UtcDateTime,
                target.ActorId
            };
            await using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleAsync<int>(new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;
        }

        private static DateTime? Utc(DateTimeOffset? value) => value?.UtcDateTime;

        private static string LockResource(RecoveryTriggerTarget target)
        {
            var identity = $"{target.EnvironmentId}\n{target.ScopeKey}\n{target.GenerationId}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            return $"HomeBudget.DataProtection.Trigger.{hash}";
        }

        private static string Format(RecoveryTriggerKind kind) => kind switch
        {
            RecoveryTriggerKind.Scheduled => "scheduled",
            RecoveryTriggerKind.OnDemand => "on-demand",
            RecoveryTriggerKind.NamedBulk => "named-bulk",
            RecoveryTriggerKind.Threshold => "threshold",
            RecoveryTriggerKind.PreOperation => "pre-operation",
            RecoveryTriggerKind.PostReconciliation => "post-reconciliation",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private sealed class TriggerStateRow
        {
            public long PendingRecordCount { get; set; }
            public long PendingByteCount { get; set; }
            public DateTime? FirstPendingUtc { get; set; }
            public DateTime? LastChangeUtc { get; set; }
            public DateTime? LastScheduledUtc { get; set; }
            public DateTime? LastTriggeredUtc { get; set; }
            public string LastTriggerReason { get; set; }
            public byte[] RowVersion { get; set; }

            public RecoveryTriggerState ToState(RecoveryTriggerTarget target) => new(
                target,
                PendingRecordCount,
                PendingByteCount,
                Offset(FirstPendingUtc),
                Offset(LastChangeUtc),
                Offset(LastScheduledUtc),
                Offset(LastTriggeredUtc),
                LastTriggerReason,
                RowVersion is null ? 0 : BitConverter.ToInt64(RowVersion.Reverse().ToArray()),
                RowVersion);

            private static DateTimeOffset? Offset(DateTime? value) => value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null;
        }
    }
}
