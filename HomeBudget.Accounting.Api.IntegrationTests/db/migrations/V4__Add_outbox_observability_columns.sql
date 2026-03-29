IF COL_LENGTH('dbo.OutboxAccountPayments', 'CorrelationId') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD CorrelationId NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'MessageId') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD MessageId NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'CausationId') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD CausationId NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'TraceParent') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD TraceParent NVARCHAR(128) NULL;
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'TraceState') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD TraceState NVARCHAR(512) NULL;
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'PublishedAt') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD PublishedAt DATETIME2(7) NULL;
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'ProcessedAt') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD ProcessedAt DATETIME2(7) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Outbox_MessageId'
      AND object_id = OBJECT_ID(N'[HomeBudget.Accounting].dbo.OutboxAccountPayments')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Outbox_MessageId
        ON [HomeBudget.Accounting].dbo.OutboxAccountPayments (MessageId)
        INCLUDE (CorrelationId, EventType, Status, CreatedAt, PublishedAt, ProcessedAt);
END
GO
