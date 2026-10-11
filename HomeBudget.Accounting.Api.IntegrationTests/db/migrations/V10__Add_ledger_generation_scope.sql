USE [HomeBudget.Accounting];
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'LedgerId') IS NULL
BEGIN
    ALTER TABLE dbo.OutboxAccountPayments ADD LedgerId NVARCHAR(128) NULL, LedgerGenerationId NVARCHAR(128) NULL;
END;
GO

IF COL_LENGTH('dbo.OutboxAccountPayments', 'LedgerId') IS NOT NULL
BEGIN
    UPDATE dbo.OutboxAccountPayments
    SET LedgerId = COALESCE(LedgerId, N'default'),
        LedgerGenerationId = COALESCE(LedgerGenerationId, N'legacy')
    WHERE LedgerId IS NULL OR LedgerGenerationId IS NULL;
    ALTER TABLE dbo.OutboxAccountPayments ALTER COLUMN LedgerId NVARCHAR(128) NOT NULL;
    ALTER TABLE dbo.OutboxAccountPayments ALTER COLUMN LedgerGenerationId NVARCHAR(128) NOT NULL;
END;
GO

IF COL_LENGTH('dbo.PaymentInboxMessages', 'LedgerId') IS NULL
BEGIN
    ALTER TABLE dbo.PaymentInboxMessages ADD LedgerId NVARCHAR(128) NULL, LedgerGenerationId NVARCHAR(128) NULL;
END;
GO

IF COL_LENGTH('dbo.PaymentInboxMessages', 'LedgerId') IS NOT NULL
BEGIN
    UPDATE dbo.PaymentInboxMessages
    SET LedgerId = COALESCE(LedgerId, N'default'),
        LedgerGenerationId = COALESCE(LedgerGenerationId, N'legacy')
    WHERE LedgerId IS NULL OR LedgerGenerationId IS NULL;
    ALTER TABLE dbo.PaymentInboxMessages ALTER COLUMN LedgerId NVARCHAR(128) NOT NULL;
    ALTER TABLE dbo.PaymentInboxMessages ALTER COLUMN LedgerGenerationId NVARCHAR(128) NOT NULL;
END;
GO

IF COL_LENGTH('dbo.TransferCommands', 'LedgerId') IS NULL
BEGIN
    ALTER TABLE dbo.TransferCommands ADD LedgerId NVARCHAR(128) NULL, LedgerGenerationId NVARCHAR(128) NULL;
END;
GO

IF COL_LENGTH('dbo.TransferCommands', 'LedgerId') IS NOT NULL
BEGIN
    UPDATE dbo.TransferCommands
    SET LedgerId = COALESCE(LedgerId, N'default'),
        LedgerGenerationId = COALESCE(LedgerGenerationId, N'legacy')
    WHERE LedgerId IS NULL OR LedgerGenerationId IS NULL;
    ALTER TABLE dbo.TransferCommands ALTER COLUMN LedgerId NVARCHAR(128) NOT NULL;
    ALTER TABLE dbo.TransferCommands ALTER COLUMN LedgerGenerationId NVARCHAR(128) NOT NULL;
END;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.OutboxAccountPayments') AND name = N'UX_Outbox_MessageId')
    DROP INDEX UX_Outbox_MessageId ON dbo.OutboxAccountPayments;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.OutboxAccountPayments') AND name = N'UX_Outbox_Account_IdempotencyKeyHash')
    DROP INDEX UX_Outbox_Account_IdempotencyKeyHash ON dbo.OutboxAccountPayments;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.OutboxAccountPayments') AND name = N'UX_Outbox_Scope_MessageId')
    CREATE UNIQUE INDEX UX_Outbox_Scope_MessageId ON dbo.OutboxAccountPayments (LedgerId, LedgerGenerationId, MessageId) WHERE MessageId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.OutboxAccountPayments') AND name = N'UX_Outbox_Scope_Account_Idempotency')
    CREATE UNIQUE INDEX UX_Outbox_Scope_Account_Idempotency ON dbo.OutboxAccountPayments (LedgerId, LedgerGenerationId, AggregateId, IdempotencyKeyHash) WHERE IdempotencyKeyHash IS NOT NULL;
GO

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.PaymentInboxMessages') AND name = N'PK_PaymentInboxMessages')
    ALTER TABLE dbo.PaymentInboxMessages DROP CONSTRAINT PK_PaymentInboxMessages;
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.PaymentInboxMessages') AND name = N'PK_PaymentInboxMessages_Scope')
    ALTER TABLE dbo.PaymentInboxMessages ADD CONSTRAINT PK_PaymentInboxMessages_Scope PRIMARY KEY (LedgerId, LedgerGenerationId, MessageId);
GO

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.TransferCommands') AND name = N'UX_TransferCommands_CommandId')
    ALTER TABLE dbo.TransferCommands DROP CONSTRAINT UX_TransferCommands_CommandId;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.TransferCommands') AND name = N'UX_TransferCommands_IdempotencyKeyHash')
    ALTER TABLE dbo.TransferCommands DROP CONSTRAINT UX_TransferCommands_IdempotencyKeyHash;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.TransferCommands') AND name = N'PK_TransferCommands')
    ALTER TABLE dbo.TransferCommands DROP CONSTRAINT PK_TransferCommands;
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.TransferCommands') AND name = N'PK_TransferCommands_Scope')
    ALTER TABLE dbo.TransferCommands ADD CONSTRAINT PK_TransferCommands_Scope PRIMARY KEY (LedgerId, LedgerGenerationId, TransferId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TransferCommands') AND name = N'UX_TransferCommands_Scope_CommandId')
    CREATE UNIQUE INDEX UX_TransferCommands_Scope_CommandId ON dbo.TransferCommands (LedgerId, LedgerGenerationId, CommandId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TransferCommands') AND name = N'UX_TransferCommands_Scope_Idempotency')
    CREATE UNIQUE INDEX UX_TransferCommands_Scope_Idempotency ON dbo.TransferCommands (LedgerId, LedgerGenerationId, IdempotencyKeyHash);
GO

USE [HomeBudget.DataProtection];
GO

IF COL_LENGTH('dbo.ActiveLedgerGenerations', 'GenerationToken') IS NULL
BEGIN
    ALTER TABLE dbo.ActiveLedgerGenerations ADD GenerationToken NVARCHAR(200) NULL;
END;
GO

UPDATE dbo.ActiveLedgerGenerations
SET GenerationToken = CASE
    WHEN EnvironmentId = N'default' AND LedgerId = N'default' AND GenerationId = N'legacy'
        THEN N'legacy-token-v1'
    ELSE CONCAT(N'generation-', CONVERT(nvarchar(36), NEWID()))
END
WHERE GenerationToken IS NULL;
ALTER TABLE dbo.ActiveLedgerGenerations ALTER COLUMN GenerationToken NVARCHAR(200) NOT NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ActiveLedgerGenerations')
      AND name = N'UX_ActiveLedgerGenerations_GenerationToken')
BEGIN
    CREATE UNIQUE INDEX UX_ActiveLedgerGenerations_GenerationToken
        ON dbo.ActiveLedgerGenerations (GenerationToken);
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ActiveLedgerGenerations WHERE EnvironmentId = N'default' AND LedgerId = N'default')
BEGIN
    INSERT dbo.ActiveLedgerGenerations
        (EnvironmentId, LedgerId, GenerationId, GenerationToken, PreviousGenerationId,
         GenerationNumber, ActivatedByOperationId, ActivatedUtc)
    VALUES
        (N'default', N'default', N'legacy', N'legacy-token-v1', NULL, 0, NULL, SYSUTCDATETIME());
END;
GO
