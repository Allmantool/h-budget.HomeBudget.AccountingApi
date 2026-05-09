USE [HomeBudget.Accounting];
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.tables
    WHERE name = N'PaymentInboxMessages'
      AND schema_id = SCHEMA_ID(N'dbo')
)
BEGIN
    CREATE TABLE [HomeBudget.Accounting].dbo.PaymentInboxMessages
    (
        MessageId    NVARCHAR(200)  NOT NULL,
        Topic        NVARCHAR(200)  NOT NULL,
        Partition    INT            NOT NULL,
        [Offset]     BIGINT         NOT NULL,
        Status       NVARCHAR(40)   NOT NULL,
        RetryCount   INT            NOT NULL CONSTRAINT DF_PaymentInboxMessages_RetryCount DEFAULT (0),
        LastError    NVARCHAR(500)  NULL,
        CreatedUtc   DATETIME2(7)   NOT NULL CONSTRAINT DF_PaymentInboxMessages_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc   DATETIME2(7)   NOT NULL CONSTRAINT DF_PaymentInboxMessages_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        ProcessedUtc DATETIME2(7)   NULL,
        RawMessage   NVARCHAR(MAX)  NULL,

        CONSTRAINT PK_PaymentInboxMessages PRIMARY KEY CLUSTERED (MessageId)
    );

    CREATE NONCLUSTERED INDEX IX_PaymentInboxMessages_Status_UpdatedUtc
        ON [HomeBudget.Accounting].dbo.PaymentInboxMessages (Status, UpdatedUtc)
        INCLUDE (Topic, Partition, [Offset], RetryCount, ProcessedUtc);

    CREATE NONCLUSTERED INDEX IX_PaymentInboxMessages_Topic_Partition_Offset
        ON [HomeBudget.Accounting].dbo.PaymentInboxMessages (Topic, Partition, [Offset]);
END
GO
