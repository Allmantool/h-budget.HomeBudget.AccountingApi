USE [HomeBudget.Accounting];
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'OperationId') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD OperationId NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'CreatedUtc') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD CreatedUtc DATETIME2(7) NULL;

    EXEC(N'
        UPDATE [HomeBudget.Accounting].dbo.OutboxAccountPayments
           SET CreatedUtc = CreatedAt
         WHERE CreatedUtc IS NULL;');

    EXEC(N'
        ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
            ALTER COLUMN CreatedUtc DATETIME2(7) NOT NULL;');
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'UpdatedUtc') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD UpdatedUtc DATETIME2(7) NULL;

    EXEC(N'
        UPDATE [HomeBudget.Accounting].dbo.OutboxAccountPayments
           SET UpdatedUtc = UpdatedAt
         WHERE UpdatedUtc IS NULL;');

    EXEC(N'
        ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
            ALTER COLUMN UpdatedUtc DATETIME2(7) NOT NULL;');
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'PublishedUtc') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD PublishedUtc DATETIME2(7) NULL;

    EXEC(N'
        UPDATE [HomeBudget.Accounting].dbo.OutboxAccountPayments
           SET PublishedUtc = PublishedAt
         WHERE PublishedUtc IS NULL
           AND PublishedAt IS NOT NULL;');
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'LockedBy') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD LockedBy NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'LockedUntilUtc') IS NULL
BEGIN
    ALTER TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
        ADD LockedUntilUtc DATETIME2(7) NULL;
END
GO

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Outbox_MessageId'
      AND object_id = OBJECT_ID(N'[HomeBudget.Accounting].dbo.OutboxAccountPayments')
)
BEGIN
    DROP INDEX IX_Outbox_MessageId
        ON [HomeBudget.Accounting].dbo.OutboxAccountPayments;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_Outbox_MessageId'
      AND object_id = OBJECT_ID(N'[HomeBudget.Accounting].dbo.OutboxAccountPayments')
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_Outbox_MessageId
        ON [HomeBudget.Accounting].dbo.OutboxAccountPayments (MessageId)
        WHERE MessageId IS NOT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Outbox_Status_Lock_CreatedUtc'
      AND object_id = OBJECT_ID(N'[HomeBudget.Accounting].dbo.OutboxAccountPayments')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Outbox_Status_Lock_CreatedUtc
        ON [HomeBudget.Accounting].dbo.OutboxAccountPayments (Status, LockedUntilUtc, CreatedUtc)
        INCLUDE (MessageId, AggregateId, OperationId, PartitionKey, EventType, RetryCount);
END
GO
