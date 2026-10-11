using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal sealed class TransferCommandStore(IBaseReadRepository reader) : ITransferCommandStore
    {
        public Task<TransferCommandRegistration> RegisterAsync(TransferCommandRegistrationRequest request)
        {
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DECLARE @WasAlreadyAccepted bit = 0;

                IF NOT EXISTS (
                    SELECT 1
                    FROM [HomeBudget.DataProtection].dbo.ActiveLedgerGenerations WITH (HOLDLOCK)
                    WHERE EnvironmentId = @EnvironmentId
                      AND LedgerId = @LedgerId
                      AND GenerationId = @LedgerGenerationId
                      AND GenerationToken = @GenerationTokenValue)
                BEGIN
                    THROW 51001, 'The ledger generation changed before durable transfer acceptance.', 1;
                END

                IF EXISTS (
                    SELECT 1 FROM dbo.TransferCommands WITH (UPDLOCK, HOLDLOCK)
                    WHERE LedgerId = @LedgerId
                      AND LedgerGenerationId = @LedgerGenerationId
                      AND IdempotencyKeyHash = @IdempotencyKeyHash)
                BEGIN
                    SET @WasAlreadyAccepted = 1;
                    UPDATE dbo.TransferCommands
                       SET LastSeenUtc = @CreatedUtc
                     WHERE LedgerId = @LedgerId
                       AND LedgerGenerationId = @LedgerGenerationId
                       AND IdempotencyKeyHash = @IdempotencyKeyHash;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.TransferCommands
                    (
                        LedgerId, LedgerGenerationId, TransferId, CommandId, IdempotencyKeyHash, RequestFingerprint,
                        SenderAccountId, RecipientAccountId, SenderOperationId, RecipientOperationId,
                        SenderCommandId, RecipientCommandId, SenderAmount, RecipientAmount,
                        SenderCurrency, RecipientCurrency, OperationDate, SourceReference,
                        CreatedUtc, LastSeenUtc
                    )
                    VALUES
                    (
                        @LedgerId, @LedgerGenerationId, @TransferId, @CommandId, @IdempotencyKeyHash, @RequestFingerprint,
                        @SenderAccountId, @RecipientAccountId, @TransferId, @TransferId,
                        @SenderCommandId, @RecipientCommandId, @SenderAmount, @RecipientAmount,
                        @SenderCurrency, @RecipientCurrency, @OperationDate, @SourceReference,
                        @CreatedUtc, @CreatedUtc
                    );

                    INSERT INTO dbo.OutboxAccountPayments
                    (
                        LedgerId, LedgerGenerationId, EventType, AggregateId, OperationId, PartitionKey, CorrelationId,
                        MessageId, IdempotencyKeyHash, RequestFingerprint, CommandType,
                        CausationId, TraceParent, TraceState, Payload, CreatedAt, UpdatedAt,
                        CreatedUtc, UpdatedUtc, Status, RetryCount
                    )
                    VALUES
                    (
                        @LedgerId, @LedgerGenerationId, @SenderEventType, @SenderAggregateId, @TransferId, @SenderPartitionKey, @CorrelationId,
                        @SenderCommandId, @SenderKeyHash, @RequestFingerprint, @CommandType,
                        @CausationId, @TraceParent, @TraceState, @SenderPayload, @CreatedUtc, @CreatedUtc,
                        @CreatedUtc, @CreatedUtc, 0, 0
                    ),
                    (
                        @LedgerId, @LedgerGenerationId, @RecipientEventType, @RecipientAggregateId, @TransferId, @RecipientPartitionKey, @CorrelationId,
                        @RecipientCommandId, @RecipientKeyHash, @RequestFingerprint, @CommandType,
                        @CausationId, @TraceParent, @TraceState, @RecipientPayload, @CreatedUtc, @CreatedUtc,
                        @CreatedUtc, @CreatedUtc, 0, 0
                    );
                END

                COMMIT TRANSACTION;

                SELECT TransferId, CommandId, RequestFingerprint, SenderCommandId, RecipientCommandId,
                       @WasAlreadyAccepted AS WasAlreadyAccepted
                  FROM dbo.TransferCommands
                 WHERE LedgerId = @LedgerId
                   AND LedgerGenerationId = @LedgerGenerationId
                   AND IdempotencyKeyHash = @IdempotencyKeyHash;";

            var sender = request.SenderOutbox;
            var recipient = request.RecipientOutbox;
            return RegisterGuardedAsync(sql, new
            {
                request.TransferId,
                request.EnvironmentId,
                request.GenerationTokenValue,
                request.LedgerId,
                request.LedgerGenerationId,
                request.CommandId,
                request.IdempotencyKeyHash,
                request.RequestFingerprint,
                request.SourceReference,
                request.SenderAccountId,
                request.RecipientAccountId,
                request.SenderAmount,
                request.RecipientAmount,
                request.SenderCurrency,
                request.RecipientCurrency,
                request.OperationDate,
                SenderCommandId = sender.MessageId,
                RecipientCommandId = recipient.MessageId,
                SenderEventType = sender.EventType,
                RecipientEventType = recipient.EventType,
                SenderAggregateId = sender.AggregateId,
                RecipientAggregateId = recipient.AggregateId,
                SenderPartitionKey = sender.PartitionKey,
                RecipientPartitionKey = recipient.PartitionKey,
                CorrelationId = sender.CorrelationId,
                SenderKeyHash = sender.IdempotencyKeyHash,
                RecipientKeyHash = recipient.IdempotencyKeyHash,
                CommandType = sender.CommandType,
                CausationId = sender.CausationId,
                TraceParent = sender.TraceParent,
                TraceState = sender.TraceState,
                SenderPayload = sender.Payload,
                RecipientPayload = recipient.Payload,
                CreatedUtc = sender.CreatedUtc
            });
        }

        private async Task<TransferCommandRegistration> RegisterGuardedAsync(string sql, object parameters)
        {
            try
            {
                return await reader.SingleAsync<TransferCommandRegistration>(sql, parameters);
            }
            catch (SqlException ex) when (ex.Number == 51001)
            {
                throw new LedgerGenerationChangedException(ex);
            }
        }

        public async Task<TransferCommandRecord> GetByIdempotencyKeyAsync(
            string idempotencyKeyHash,
            LedgerGenerationScope generation)
        {
            var rows = await reader.GetAsync<TransferCommandRecord>(
                SelectSql + ScopeWhere + " AND transfer.IdempotencyKeyHash = @IdempotencyKeyHash;",
                new { generation.LedgerId, generation.LedgerGenerationId, IdempotencyKeyHash = idempotencyKeyHash });
            return rows.SingleOrDefault();
        }

        public async Task<TransferCommandRecord> GetAsync(
            Guid transferId,
            string commandId,
            LedgerGenerationScope generation)
        {
            var rows = await reader.GetAsync<TransferCommandRecord>(
                SelectSql + ScopeWhere + " AND transfer.TransferId = @TransferId AND transfer.CommandId = @CommandId;",
                new { generation.LedgerId, generation.LedgerGenerationId, TransferId = transferId, CommandId = commandId });
            return rows.SingleOrDefault();
        }

        public async Task<TransferCommandRecord> GetByTransferIdAsync(
            Guid transferId,
            LedgerGenerationScope generation)
        {
            var rows = await reader.GetAsync<TransferCommandRecord>(
                SelectSql + ScopeWhere + " AND transfer.TransferId = @TransferId;",
                new { generation.LedgerId, generation.LedgerGenerationId, TransferId = transferId });
            return rows.SingleOrDefault();
        }

        private const string ScopeWhere = @"
             WHERE transfer.LedgerId = @LedgerId
               AND transfer.LedgerGenerationId = @LedgerGenerationId";

        private const string SelectSql = @"
            SELECT transfer.LedgerId, transfer.LedgerGenerationId,
                   transfer.TransferId, transfer.CommandId, transfer.RequestFingerprint,
                   transfer.SenderAccountId, transfer.RecipientAccountId,
                   transfer.SenderOperationId, transfer.RecipientOperationId,
                   transfer.SenderCommandId, transfer.RecipientCommandId,
                   transfer.SenderAmount, transfer.RecipientAmount,
                   transfer.SenderCurrency, transfer.RecipientCurrency,
                   transfer.OperationDate, transfer.SourceReference,
                   transfer.CreatedUtc AS AcceptedUtc,
                   sender.Status AS SenderStatus, recipient.Status AS RecipientStatus,
                   sender.PublishedUtc AS SenderPublishedUtc, recipient.PublishedUtc AS RecipientPublishedUtc,
                   sender.PersistedUtc AS SenderPersistedUtc, recipient.PersistedUtc AS RecipientPersistedUtc,
                   sender.ProjectedUtc AS SenderProjectedUtc, recipient.ProjectedUtc AS RecipientProjectedUtc,
                   sender.LastError AS SenderError, recipient.LastError AS RecipientError
              FROM dbo.TransferCommands transfer
              JOIN dbo.OutboxAccountPayments sender ON sender.MessageId = transfer.SenderCommandId
                   AND sender.LedgerId = transfer.LedgerId
                   AND sender.LedgerGenerationId = transfer.LedgerGenerationId
              JOIN dbo.OutboxAccountPayments recipient ON recipient.MessageId = transfer.RecipientCommandId
                   AND recipient.LedgerId = transfer.LedgerId
                   AND recipient.LedgerGenerationId = transfer.LedgerGenerationId";
    }
}
