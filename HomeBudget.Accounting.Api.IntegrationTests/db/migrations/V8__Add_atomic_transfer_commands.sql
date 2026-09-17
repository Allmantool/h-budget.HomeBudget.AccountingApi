USE [HomeBudget.Accounting];
GO

IF OBJECT_ID(N'dbo.TransferCommands', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TransferCommands
    (
        TransferId UNIQUEIDENTIFIER NOT NULL,
        CommandId NVARCHAR(100) NOT NULL,
        IdempotencyKeyHash NVARCHAR(64) NOT NULL,
        RequestFingerprint NVARCHAR(64) NOT NULL,
        SenderAccountId UNIQUEIDENTIFIER NOT NULL,
        RecipientAccountId UNIQUEIDENTIFIER NOT NULL,
        SenderOperationId UNIQUEIDENTIFIER NOT NULL,
        RecipientOperationId UNIQUEIDENTIFIER NOT NULL,
        SenderCommandId NVARCHAR(100) NOT NULL,
        RecipientCommandId NVARCHAR(100) NOT NULL,
        SenderAmount DECIMAL(38, 18) NOT NULL,
        RecipientAmount DECIMAL(38, 18) NOT NULL,
        SenderCurrency NVARCHAR(16) NOT NULL,
        RecipientCurrency NVARCHAR(16) NOT NULL,
        OperationDate DATE NOT NULL,
        SourceReference NVARCHAR(500) NOT NULL,
        CreatedUtc DATETIME2(7) NOT NULL,
        LastSeenUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_TransferCommands PRIMARY KEY (TransferId),
        CONSTRAINT UX_TransferCommands_CommandId UNIQUE (CommandId),
        CONSTRAINT UX_TransferCommands_IdempotencyKeyHash UNIQUE (IdempotencyKeyHash)
    );

    CREATE INDEX IX_TransferCommands_Accounts_Date
        ON dbo.TransferCommands (SenderAccountId, RecipientAccountId, OperationDate);
END
GO

