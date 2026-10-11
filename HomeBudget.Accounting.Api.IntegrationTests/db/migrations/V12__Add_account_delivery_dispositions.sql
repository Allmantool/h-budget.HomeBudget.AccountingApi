-- evolve-tx-off
USE [HomeBudget.DataProtection];
GO

IF OBJECT_ID(N'dbo.AccountDeliveryDispositions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountDeliveryDispositions
    (
        DispositionId BIGINT IDENTITY(1, 1) NOT NULL,
        MessageId NVARCHAR(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        LedgerId NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        LedgerGenerationId NVARCHAR(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PayloadSha256 CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Reason NVARCHAR(64) NOT NULL,
        FirstTopic NVARCHAR(249) NOT NULL,
        FirstPartition INT NOT NULL,
        FirstOffset BIGINT NOT NULL,
        LastTopic NVARCHAR(249) NOT NULL,
        LastPartition INT NOT NULL,
        LastOffset BIGINT NOT NULL,
        FirstDisposedUtc DATETIME2(7) NOT NULL,
        LastDisposedUtc DATETIME2(7) NOT NULL,
        DeliveryCount INT NOT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_AccountDeliveryDispositions PRIMARY KEY (DispositionId),
        CONSTRAINT UX_AccountDeliveryDispositions_MessageScope
            UNIQUE (MessageId, LedgerId, LedgerGenerationId),
        CONSTRAINT CK_AccountDeliveryDispositions_PayloadSha256 CHECK
            (LEN(PayloadSha256) = 64 AND PayloadSha256 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_AccountDeliveryDispositions_Reason CHECK
            (Reason IN (N'stale-generation')),
        CONSTRAINT CK_AccountDeliveryDispositions_DeliveryCount CHECK (DeliveryCount >= 1),
        CONSTRAINT CK_AccountDeliveryDispositions_Timestamps CHECK
            (LastDisposedUtc >= FirstDisposedUtc)
    );

    CREATE INDEX IX_AccountDeliveryDispositions_ScopeDisposedUtc
        ON dbo.AccountDeliveryDispositions
            (LedgerId, LedgerGenerationId, LastDisposedUtc DESC)
        INCLUDE (MessageId, Reason, DeliveryCount);
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_AccountDeliveryDispositions_ImmutableBinding
ON dbo.AccountDeliveryDispositions
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
          FROM inserted AS current_row
          INNER JOIN deleted AS previous_row
             ON previous_row.DispositionId = current_row.DispositionId
         WHERE current_row.MessageId <> previous_row.MessageId
            OR current_row.LedgerId <> previous_row.LedgerId
            OR current_row.LedgerGenerationId <> previous_row.LedgerGenerationId
            OR current_row.PayloadSha256 <> previous_row.PayloadSha256
            OR current_row.Reason <> previous_row.Reason
            OR current_row.FirstTopic <> previous_row.FirstTopic
            OR current_row.FirstPartition <> previous_row.FirstPartition
            OR current_row.FirstOffset <> previous_row.FirstOffset
            OR current_row.FirstDisposedUtc <> previous_row.FirstDisposedUtc
            OR current_row.DeliveryCount < previous_row.DeliveryCount
            OR current_row.LastDisposedUtc < previous_row.LastDisposedUtc
    )
    BEGIN
        THROW 51005, N'Account delivery disposition identity and terminal binding are immutable.', 1;
    END;
END;
GO

USE [HomeBudget.Accounting];
GO
