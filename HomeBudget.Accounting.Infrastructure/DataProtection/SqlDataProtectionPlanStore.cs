using System;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class SqlDataProtectionPlanStore : IDataProtectionPlanStore
    {
        private const string ConsumeAndEnqueueSql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            DECLARE @Outcome nvarchar(32);
            DECLARE @StoredOperationKind nvarchar(64);
            DECLARE @StoredEnvironmentId nvarchar(128);
            DECLARE @StoredLedgerId nvarchar(128);
            DECLARE @StoredGenerationId nvarchar(128);
            DECLARE @StoredPlanFingerprint char(64);
            DECLARE @StoredActorId nvarchar(256);
            DECLARE @StoredTokenHash char(64);
            DECLARE @ExpiresUtc datetime2(7);
            DECLARE @ConsumedUtc datetime2(7);

            SELECT @StoredOperationKind = OperationKind,
                   @StoredEnvironmentId = EnvironmentId,
                   @StoredLedgerId = LedgerId,
                   @StoredGenerationId = GenerationId,
                   @StoredPlanFingerprint = PlanFingerprint,
                   @StoredActorId = ActorId,
                   @StoredTokenHash = ConfirmationTokenHash,
                   @ExpiresUtc = ExpiresUtc,
                   @ConsumedUtc = ConsumedUtc
              FROM dbo.DataProtectionPlans WITH (UPDLOCK, HOLDLOCK)
             WHERE PlanId = @PlanId;

            IF @StoredOperationKind IS NULL
                SET @Outcome = N'plan-not-found';
            ELSE IF @ConsumedUtc IS NOT NULL
                SET @Outcome = N'confirmation-consumed';
            ELSE IF @ExpiresUtc <= @CreatedUtc
                SET @Outcome = N'plan-expired';
            ELSE IF @StoredOperationKind <> @OperationKind
                 OR @StoredEnvironmentId <> @EnvironmentId
                 OR ISNULL(@StoredLedgerId, N'') <> ISNULL(@LedgerId, N'')
                 OR ISNULL(@StoredGenerationId, N'') <> ISNULL(@GenerationId, N'')
                 OR @StoredPlanFingerprint <> @PlanFingerprint
                 OR @StoredActorId <> @ActorId
                 OR @StoredTokenHash <> @ConfirmationTokenHash
                SET @Outcome = N'binding-mismatch';
            ELSE
            BEGIN
                INSERT INTO dbo.DataProtectionOperations
                    (OperationId, OperationKind, ScopeKind, ScopeKey, EnvironmentId, LedgerId,
                     GenerationId, PlanFingerprint, State, CreatedUtc, UpdatedUtc)
                VALUES
                    (@OperationId, @OperationKind, @ScopeKind, @ScopeKey, @EnvironmentId, @LedgerId,
                     @GenerationId, @PlanFingerprint, N'queued', @CreatedUtc, @CreatedUtc);

                UPDATE dbo.DataProtectionPlans
                   SET ConsumedUtc = @CreatedUtc, ConsumedByOperationId = @OperationId
                 WHERE PlanId = @PlanId;

                INSERT INTO dbo.ProtectedAuditEntries
                    (AuditId, OperationId, ActorId, EventType, ScopeKey, TargetEnvironmentId,
                     TargetLedgerId, TargetGenerationId, PlanFingerprint, ConfirmationFingerprint,
                     Result, OccurredUtc)
                VALUES
                    (NEWID(), @OperationId, @ActorId, N'operation-created', @ScopeKey, @EnvironmentId,
                     @LedgerId, @GenerationId, @PlanFingerprint, @ConfirmationTokenHash,
                     N'queued', @CreatedUtc);

                SET @Outcome = N'accepted';
            END;

            COMMIT TRANSACTION;
            SELECT @Outcome;";

        private readonly string _connectionString;

        public SqlDataProtectionPlanStore(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            };
            _connectionString = builder.ConnectionString;
        }

        public async Task CreateAsync(DataProtectionPlanRecord plan, CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO dbo.DataProtectionPlans
                    (PlanId, OperationKind, ScopeKind, ScopeKey, EnvironmentId, LedgerId,
                     GenerationId, PlanFingerprint, ActorId, ConfirmationTokenHash, CreatedUtc, ExpiresUtc)
                VALUES
                    (@PlanId, @OperationKind, @ScopeKind, @ScopeKey, @EnvironmentId, @LedgerId,
                     @GenerationId, @PlanFingerprint, @ActorId, @ConfirmationTokenHash, @CreatedUtc, @ExpiresUtc);";
            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(new CommandDefinition(sql, plan, cancellationToken: cancellationToken));
        }

        public async Task<DataProtectionOperationStartResult> TryConsumeAndEnqueueAsync(
            DataProtectionPlanRecord expectedPlan,
            DataProtectionOperationRequest operation,
            CancellationToken cancellationToken)
        {
            var parameters = new
            {
                expectedPlan.PlanId,
                expectedPlan.OperationKind,
                expectedPlan.ScopeKind,
                expectedPlan.ScopeKey,
                expectedPlan.EnvironmentId,
                expectedPlan.LedgerId,
                expectedPlan.GenerationId,
                expectedPlan.PlanFingerprint,
                expectedPlan.ActorId,
                expectedPlan.ConfirmationTokenHash,
                operation.OperationId,
                operation.CreatedUtc
            };
            await using var connection = new SqlConnection(_connectionString);
            var outcome = await connection.QuerySingleAsync<string>(
                new CommandDefinition(ConsumeAndEnqueueSql, parameters, cancellationToken: cancellationToken));

            return new DataProtectionOperationStartResult
            {
                Outcome = ParseOutcome(outcome),
                Operation = outcome == "accepted"
                    ? await ReadOperationAsync(connection, operation.OperationId, cancellationToken)
                    : null
            };
        }

        private static async Task<DataProtectionOperation> ReadOperationAsync(
            SqlConnection connection,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT OperationId, OperationKind, ScopeKind, ScopeKey, EnvironmentId, LedgerId,
                       GenerationId, PlanFingerprint, State AS StateValue, Stage, CancellationRequested,
                       CreatedUtc, UpdatedUtc, RowVersion
                  FROM dbo.DataProtectionOperations
                 WHERE OperationId = @OperationId;";
            return await connection.QuerySingleAsync<DataProtectionOperation>(new CommandDefinition(
                sql,
                new { OperationId = operationId },
                cancellationToken: cancellationToken));
        }

        private static DataProtectionOperationStartOutcome ParseOutcome(string outcome)
        {
            return outcome switch
            {
                "accepted" => DataProtectionOperationStartOutcome.Accepted,
                "plan-not-found" => DataProtectionOperationStartOutcome.PlanNotFound,
                "plan-expired" => DataProtectionOperationStartOutcome.PlanExpired,
                "confirmation-consumed" => DataProtectionOperationStartOutcome.ConfirmationConsumed,
                _ => DataProtectionOperationStartOutcome.BindingMismatch
            };
        }
    }
}
