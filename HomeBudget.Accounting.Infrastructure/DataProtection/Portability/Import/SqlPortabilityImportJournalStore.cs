using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed class SqlPortabilityImportJournalStore : IPortabilityImportJournalStore
    {
        private const string RunColumns = @"
            OperationId, ExportId, PackageSha256, FormatVersion, Completeness,
            ImportMode, TargetEnvironmentId, TargetLedgerId, TargetGenerationId,
            TargetGenerationNumber, PreOperationRecoverySetId, EntityCount, State,
            CreatedUtc, UpdatedUtc";
        private readonly string _connectionString;

        public SqlPortabilityImportJournalStore(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            };
            _connectionString = builder.ConnectionString;
        }

        public async Task<PortabilityImportJournal> CreateOrReadAsync(
            PortabilityImportJournalRequest request,
            CancellationToken cancellationToken)
        {
            ValidateRequest(request);
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var existing = await ReadAsync(
                    connection,
                    transaction,
                    request.OperationId,
                    true,
                    cancellationToken).ConfigureAwait(false);
                if (existing != null)
                {
                    EnsureSameBinding(existing, request);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return existing;
                }

                await EnsureOperationBindingAsync(
                    connection,
                    transaction,
                    request,
                    cancellationToken).ConfigureAwait(false);
                await InsertRunAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
                await InsertOutcomesAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
                await InsertAuditAsync(connection, transaction, request, cancellationToken).ConfigureAwait(false);
                var created = await ReadAsync(
                    connection,
                    transaction,
                    request.OperationId,
                    false,
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return created;
            }
            catch (SqlException exception) when (exception.Number is 2601 or 2627)
            {
                throw new PortabilityImportJournalConflictException("import.journal-binding-conflict");
            }
        }

        public async Task<PortabilityImportJournal> GetAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await ReadAsync(
                connection,
                transaction: null,
                operationId,
                lockBinding: false,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> TryTransitionOutcomeAsync(
            PortabilityImportOutcomeTransition transition,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(transition);
            const string sql = @"
                UPDATE dbo.ImportOutcomes
                   SET State = @NextState,
                       ErrorClassification = @ErrorClassification,
                       UpdatedUtc = @UpdatedUtc
                 WHERE OperationId = @OperationId
                   AND EntityType = @EntityType
                   AND SourceEntityId = @SourceEntityId
                   AND Fingerprint = @Fingerprint
                   AND IdempotencyKey = @IdempotencyKey
                   AND State = @ExpectedState;";
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                transition,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed == 1)
            {
                return true;
            }

            await EnsureOutcomeTransitionIsIdempotentAsync(
                connection,
                transition,
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        public async Task<bool> TryTransitionRunAsync(
            Guid operationId,
            string expectedState,
            string nextState,
            DateTime updatedUtc,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                UPDATE dbo.PortabilityImportRuns
                   SET State = @NextState, UpdatedUtc = @UpdatedUtc
                 WHERE OperationId = @OperationId AND State = @ExpectedState;";
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { OperationId = operationId, ExpectedState = expectedState, NextState = nextState, UpdatedUtc = updatedUtc },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed == 1)
            {
                return true;
            }

            var state = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT State FROM dbo.PortabilityImportRuns WHERE OperationId = @OperationId;",
                new { OperationId = operationId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (state == nextState)
            {
                return false;
            }

            throw new PortabilityImportJournalConflictException("import.run-state-conflict");
        }

        public async Task<bool> RecordReconciliationAsync(
            PortabilityImportReconciliationRecord record,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(record);
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);
            var run = await connection.QuerySingleOrDefaultAsync<ReconciliationRun>(new CommandDefinition(
                @"SELECT State, EntityCount
                    FROM dbo.PortabilityImportRuns WITH (UPDLOCK, HOLDLOCK)
                   WHERE OperationId = @OperationId;",
                new { record.OperationId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var nextState = record.Succeeded ? "succeeded" : "failed";
            if (run == null)
            {
                throw new PortabilityImportJournalConflictException("import.journal-missing");
            }

            if (run.State == nextState)
            {
                await EnsureSameReconciliationAsync(
                    connection,
                    transaction,
                    record,
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            if (run.State != "awaiting-reconciliation")
            {
                throw new PortabilityImportJournalConflictException("import.reconciliation-state-conflict");
            }

            if (record.Succeeded)
            {
                var reconciled = await connection.ExecuteAsync(new CommandDefinition(
                    @"UPDATE dbo.ImportOutcomes
                         SET State = N'reconciled', UpdatedUtc = @OccurredUtc
                       WHERE OperationId = @OperationId AND State = N'projected';",
                    record,
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (reconciled != run.EntityCount)
                {
                    throw new PortabilityImportJournalConflictException("import.reconciliation-state-conflict");
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(
                @"UPDATE dbo.PortabilityImportRuns
                     SET State = @NextState, UpdatedUtc = @OccurredUtc
                   WHERE OperationId = @OperationId AND State = N'awaiting-reconciliation';",
                new { record.OperationId, NextState = nextState, record.OccurredUtc },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            await InsertReconciliationAuditAsync(
                connection,
                transaction,
                record,
                nextState,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        private static async Task InsertRunAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            PortabilityImportJournalRequest request,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO dbo.PortabilityImportRuns
                    (OperationId, ExportId, PackageSha256, FormatVersion, Completeness,
                     ImportMode, TargetEnvironmentId, TargetLedgerId, TargetGenerationId,
                     TargetGenerationNumber, PreOperationRecoverySetId, EntityCount, State,
                     CreatedUtc, UpdatedUtc)
                VALUES
                    (@OperationId, @ExportId, @PackageSha256, @FormatVersion, @Completeness,
                     @Mode, @TargetEnvironmentId, @TargetLedgerId, @TargetGenerationId,
                     @TargetGenerationNumber, @PreOperationRecoverySetId, @EntityCount, N'journaled',
                     @CreatedUtc, @CreatedUtc);";
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                Parameters(request),
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        private static async Task EnsureOperationBindingAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            PortabilityImportJournalRequest request,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT OperationKind, ScopeKind, EnvironmentId, LedgerId, GenerationId
                  FROM dbo.DataProtectionOperations WITH (UPDLOCK, HOLDLOCK)
                 WHERE OperationId = @OperationId;";
            var operation = await connection.QuerySingleOrDefaultAsync<OperationBinding>(new CommandDefinition(
                sql,
                new { request.OperationId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var expectedKind = request.Mode == "replace" ? "replace" : "import";
            if (operation == null
                || operation.OperationKind != expectedKind
                || operation.ScopeKind != "ledger"
                || operation.EnvironmentId != request.TargetEnvironmentId
                || operation.LedgerId != request.TargetLedgerId
                || operation.GenerationId != request.TargetGenerationId)
            {
                throw new PortabilityImportJournalConflictException("import.operation-binding-conflict");
            }
        }

        private static async Task InsertOutcomesAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            PortabilityImportJournalRequest request,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO dbo.ImportOutcomes
                    (ImportOutcomeId, OperationId, EntityType, SourceEntityId, TargetEntityId,
                     IdempotencyKey, Fingerprint, State, CreatedUtc, UpdatedUtc)
                VALUES
                    (@ImportOutcomeId, @OperationId, @EntityType, @SourceEntityId, @SourceEntityId,
                     @IdempotencyKey, @Fingerprint, N'validated', @CreatedUtc, @CreatedUtc);";
            foreach (var entity in request.Preflight.Entities)
            {
                var parameters = new
                {
                    ImportOutcomeId = Guid.NewGuid(),
                    request.OperationId,
                    entity.EntityType,
                    entity.SourceEntityId,
                    entity.IdempotencyKey,
                    entity.Fingerprint,
                    request.CreatedUtc
                };
                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    parameters,
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }

        private static async Task InsertAuditAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            PortabilityImportJournalRequest request,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO dbo.ProtectedAuditEntries
                    (AuditId, OperationId, ActorId, EventType, ScopeKey, TargetEnvironmentId,
                     TargetLedgerId, TargetGenerationId, SourceChecksum, RecoverySetId,
                     PlanFingerprint, Result, OccurredUtc)
                SELECT NEWID(), operation.OperationId, @ActorId, N'import-journal-created',
                       operation.ScopeKey, @TargetEnvironmentId, @TargetLedgerId,
                       @TargetGenerationId, @PackageSha256, @PreOperationRecoverySetId,
                       operation.PlanFingerprint, N'journaled', @CreatedUtc
                  FROM dbo.DataProtectionOperations AS operation
                 WHERE operation.OperationId = @OperationId;";
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                Parameters(request),
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed != 1)
            {
                throw new PortabilityImportJournalConflictException("import.operation-missing");
            }
        }

        private static async Task<PortabilityImportJournal> ReadAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            Guid operationId,
            bool lockBinding,
            CancellationToken cancellationToken)
        {
            var hint = lockBinding ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
            var sql = "SELECT " + RunColumns + " FROM dbo.PortabilityImportRuns" + hint
                + " WHERE OperationId = @OperationId;";
            var run = await connection.QuerySingleOrDefaultAsync<PortabilityImportJournal>(new CommandDefinition(
                sql,
                new { OperationId = operationId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (run == null)
            {
                return null;
            }

            const string outcomeSql = @"
                SELECT ImportOutcomeId, OperationId, EntityType, SourceEntityId, TargetEntityId,
                       IdempotencyKey, Fingerprint, State, ErrorClassification, CreatedUtc, UpdatedUtc
                  FROM dbo.ImportOutcomes
                 WHERE OperationId = @OperationId
                 ORDER BY EntityType, SourceEntityId;";
            var outcomes = await connection.QueryAsync<PortabilityImportOutcome>(new CommandDefinition(
                outcomeSql,
                new { OperationId = operationId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            run.Outcomes = outcomes.AsList();
            return run;
        }

        private static object Parameters(PortabilityImportJournalRequest request) => new
        {
            request.OperationId,
            request.Preflight.ExportId,
            request.Preflight.PackageSha256,
            request.Preflight.FormatVersion,
            request.Preflight.Completeness,
            request.Mode,
            request.TargetEnvironmentId,
            request.TargetLedgerId,
            request.TargetGenerationId,
            request.TargetGenerationNumber,
            request.PreOperationRecoverySetId,
            EntityCount = request.Preflight.Entities.Count,
            request.ActorId,
            request.CreatedUtc
        };

        private static async Task EnsureOutcomeTransitionIsIdempotentAsync(
            SqlConnection connection,
            PortabilityImportOutcomeTransition transition,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT Fingerprint, IdempotencyKey, State
                  FROM dbo.ImportOutcomes
                 WHERE OperationId = @OperationId
                   AND EntityType = @EntityType
                   AND SourceEntityId = @SourceEntityId;";
            var outcome = await connection.QuerySingleOrDefaultAsync<OutcomeBinding>(new CommandDefinition(
                sql,
                transition,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (outcome == null
                || outcome.Fingerprint != transition.Fingerprint
                || outcome.IdempotencyKey != transition.IdempotencyKey
                || outcome.State != transition.NextState)
            {
                throw new PortabilityImportJournalConflictException("import.outcome-state-conflict");
            }
        }

        private static async Task InsertReconciliationAuditAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            PortabilityImportReconciliationRecord record,
            string result,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO dbo.ProtectedAuditEntries
                    (AuditId, OperationId, ActorId, EventType, ScopeKey, TargetEnvironmentId,
                     TargetLedgerId, TargetGenerationId, SourceChecksum, PlanFingerprint,
                     Result, ErrorClassification, OccurredUtc)
                SELECT NEWID(), operation.OperationId, @ActorId, N'import-reconciliation',
                       operation.ScopeKey, operation.EnvironmentId, operation.LedgerId,
                       operation.GenerationId, @EvidenceSha256, operation.PlanFingerprint,
                       @Result, @ErrorCode, @OccurredUtc
                  FROM dbo.DataProtectionOperations AS operation
                 WHERE operation.OperationId = @OperationId;";
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    record.OperationId,
                    record.ActorId,
                    record.EvidenceSha256,
                    Result = result,
                    record.ErrorCode,
                    record.OccurredUtc
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed != 1)
            {
                throw new PortabilityImportJournalConflictException("import.operation-missing");
            }
        }

        private static async Task EnsureSameReconciliationAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            PortabilityImportReconciliationRecord record,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT COUNT(1)
                  FROM dbo.ProtectedAuditEntries
                 WHERE OperationId = @OperationId
                   AND EventType = N'import-reconciliation'
                   AND SourceChecksum = @EvidenceSha256
                   AND Result = @Result
                   AND ((ErrorClassification IS NULL AND @ErrorCode IS NULL)
                     OR ErrorClassification = @ErrorCode);";
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                sql,
                new
                {
                    record.OperationId,
                    record.EvidenceSha256,
                    Result = record.Succeeded ? "succeeded" : "failed",
                    record.ErrorCode
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (count != 1)
            {
                throw new PortabilityImportJournalConflictException("import.reconciliation-binding-conflict");
            }
        }

        private static void EnsureSameBinding(
            PortabilityImportJournal existing,
            PortabilityImportJournalRequest request)
        {
            var expected = request.Preflight.Entities
                .ToDictionary(value => $"{value.EntityType}\0{value.SourceEntityId}", StringComparer.Ordinal);
            var sameOutcomes = existing.Outcomes.Count == expected.Count
                && existing.Outcomes.All(value =>
                    expected.TryGetValue($"{value.EntityType}\0{value.SourceEntityId}", out var entity)
                    && entity.Fingerprint == value.Fingerprint
                    && entity.IdempotencyKey == value.IdempotencyKey);
            if (existing.ExportId != request.Preflight.ExportId
                || existing.PackageSha256 != request.Preflight.PackageSha256
                || existing.FormatVersion != request.Preflight.FormatVersion
                || existing.Completeness != request.Preflight.Completeness
                || existing.ImportMode != request.Mode
                || existing.TargetEnvironmentId != request.TargetEnvironmentId
                || existing.TargetLedgerId != request.TargetLedgerId
                || existing.TargetGenerationId != request.TargetGenerationId
                || existing.TargetGenerationNumber != request.TargetGenerationNumber
                || existing.PreOperationRecoverySetId != request.PreOperationRecoverySetId
                || !sameOutcomes)
            {
                throw new PortabilityImportJournalConflictException("import.journal-binding-conflict");
            }
        }

        private static void ValidateRequest(PortabilityImportJournalRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Preflight);
            if (request.OperationId == Guid.Empty
                || request.Mode is not "empty-target" and not "resume" and not "replace"
                || string.IsNullOrWhiteSpace(request.ActorId)
                || string.IsNullOrWhiteSpace(request.TargetEnvironmentId)
                || string.IsNullOrWhiteSpace(request.TargetLedgerId)
                || string.IsNullOrWhiteSpace(request.TargetGenerationId)
                || request.TargetGenerationNumber < 0
                || request.Preflight.PackageSha256?.Length != 64
                || (request.Mode == "replace" && request.PreOperationRecoverySetId == null))
            {
                throw new ArgumentException("A complete non-validation import journal binding is required.", nameof(request));
            }
        }

        private sealed class OperationBinding
        {
            public string OperationKind { get; init; }

            public string ScopeKind { get; init; }

            public string EnvironmentId { get; init; }

            public string LedgerId { get; init; }

            public string GenerationId { get; init; }
        }

        private sealed class OutcomeBinding
        {
            public string Fingerprint { get; init; }

            public string IdempotencyKey { get; init; }

            public string State { get; init; }
        }

        private sealed class ReconciliationRun
        {
            public string State { get; init; }

            public int EntityCount { get; init; }
        }
    }
}
