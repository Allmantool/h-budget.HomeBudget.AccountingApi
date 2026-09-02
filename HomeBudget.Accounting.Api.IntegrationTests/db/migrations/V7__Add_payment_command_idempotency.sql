USE [HomeBudget.Accounting];
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'IdempotencyKeyHash') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD IdempotencyKeyHash NVARCHAR(64) NULL,
            RequestFingerprint NVARCHAR(64) NULL,
            CommandType NVARCHAR(40) NULL,
            PersistedUtc DATETIME2(7) NULL,
            ProjectedUtc DATETIME2(7) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_Outbox_Account_IdempotencyKeyHash'
      AND object_id = OBJECT_ID(N'[HomeBudget.Accounting].dbo.OutboxAccountPayments')
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_Outbox_Account_IdempotencyKeyHash
        ON [HomeBudget.Accounting].dbo.OutboxAccountPayments (AggregateId, IdempotencyKeyHash)
        WHERE IdempotencyKeyHash IS NOT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Outbox_CommandStatus'
      AND object_id = OBJECT_ID(N'[HomeBudget.Accounting].dbo.OutboxAccountPayments')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Outbox_CommandStatus
        ON [HomeBudget.Accounting].dbo.OutboxAccountPayments (AggregateId, MessageId, Status)
        INCLUDE (OperationId, CommandType, CreatedUtc, PublishedUtc, PersistedUtc, ProjectedUtc);
END
GO
