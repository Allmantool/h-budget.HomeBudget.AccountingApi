using System;
using System.Linq;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
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

                IF EXISTS (
                    SELECT 1 FROM dbo.TransferCommands WITH (UPDLOCK, HOLDLOCK)
                    WHERE IdempotencyKeyHash = @IdempotencyKeyHash)
                BEGIN
                    SET @WasAlreadyAccepted = 1;
                    UPDATE dbo.TransferCommands
                       SET LastSeenUtc = @CreatedUtc
                     WHERE IdempotencyKeyHash = @IdempotencyKeyHash;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.TransferCommands
                    (
                        TransferId, CommandId, IdempotencyKeyHash, RequestFingerprint,
                        SenderAccountId, RecipientAccountId, SenderOperationId, RecipientOperationId,
                        SenderCommandId, RecipientCommandId, SenderAmount, RecipientAmount,
                        SenderCurrency, RecipientCurrency, OperationDate, SourceReference,
                        CreatedUtc, LastSeenUtc
                    )
                    VALUES
                    (
                        @TransferId, @CommandId, @IdempotencyKeyHash, @RequestFingerprint,
                        @SenderAccountId, @RecipientAccountId, @TransferId, @TransferId,
                        @SenderCommandId, @RecipientCommandId, @SenderAmount, @RecipientAmount,
                        @SenderCurrency, @RecipientCurrency, @OperationDate, @SourceReference,
                        @CreatedUtc, @CreatedUtc
                    );

                    INSERT INTO dbo.OutboxAccountPayments
                    (
                        EventType, AggregateId, OperationId, PartitionKey, CorrelationId,
                        MessageId, IdempotencyKeyHash, RequestFingerprint, CommandType,
                        CausationId, TraceParent, TraceState, Payload, CreatedAt, UpdatedAt,
                        CreatedUtc, UpdatedUtc, Status, RetryCount
                    )
                    VALUES
                    (
                        @SenderEventType, @SenderAggregateId, @TransferId, @SenderPartitionKey, @CorrelationId,
                        @SenderCommandId, @SenderKeyHash, @RequestFingerprint, @CommandType,
                        @CausationId, @TraceParent, @TraceState, @SenderPayload, @CreatedUtc, @CreatedUtc,
                        @CreatedUtc, @CreatedUtc, 0, 0
                    ),
                    (
                        @RecipientEventType, @RecipientAggregateId, @TransferId, @RecipientPartitionKey, @CorrelationId,
                        @RecipientCommandId, @RecipientKeyHash, @RequestFingerprint, @CommandType,
                        @CausationId, @TraceParent, @TraceState, @RecipientPayload, @CreatedUtc, @CreatedUtc,
                        @CreatedUtc, @CreatedUtc, 0, 0
                    );
                END

                COMMIT TRANSACTION;

                SELECT TransferId, CommandId, RequestFingerprint, SenderCommandId, RecipientCommandId,
                       @WasAlreadyAccepted AS WasAlreadyAccepted
                  FROM dbo.TransferCommands
                 WHERE IdempotencyKeyHash = @IdempotencyKeyHash;";

            var sender = request.SenderOutbox;
            var recipient = request.RecipientOutbox;
            return reader.SingleAsync<TransferCommandRegistration>(sql, new
            {
                request.TransferId,
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

        public async Task<TransferCommandRecord> GetByIdempotencyKeyAsync(string idempotencyKeyHash)
        {
            var rows = await reader.GetAsync<TransferCommandRecord>(SelectSql + " WHERE transfer.IdempotencyKeyHash = @IdempotencyKeyHash;", new { IdempotencyKeyHash = idempotencyKeyHash });
            return rows.SingleOrDefault();
        }

        public async Task<TransferCommandRecord> GetAsync(Guid transferId, string commandId)
        {
            var rows = await reader.GetAsync<TransferCommandRecord>(SelectSql + " WHERE transfer.TransferId = @TransferId AND transfer.CommandId = @CommandId;", new { TransferId = transferId, CommandId = commandId });
            return rows.SingleOrDefault();
        }

        public async Task<TransferCommandRecord> GetByTransferIdAsync(Guid transferId)
        {
            var rows = await reader.GetAsync<TransferCommandRecord>(SelectSql + " WHERE transfer.TransferId = @TransferId;", new { TransferId = transferId });
            return rows.SingleOrDefault();
        }

        private const string SelectSql = @"
            SELECT transfer.TransferId, transfer.CommandId, transfer.RequestFingerprint,
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
              JOIN dbo.OutboxAccountPayments recipient ON recipient.MessageId = transfer.RecipientCommandId";
    }
}
