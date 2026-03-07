IF NOT EXISTS (
    SELECT 1
    FROM sys.tables
    WHERE name = N'OutboxAccountPayments'
      AND schema_id = SCHEMA_ID(N'dbo')
)
BEGIN
    CREATE TABLE [HomeBudget.Accounting].dbo.OutboxAccountPayments
    (
        Id 		UNIQUEIDENTIFIER 	NOT NULL CONSTRAINT DF_Outbox_Id DEFAULT NEWSEQUENTIALID(),

        AggregateId  NVARCHAR(100)  NOT NULL,
        EventType    NVARCHAR(200)  NOT NULL,
        Payload      NVARCHAR(MAX)  NOT NULL,
        PartitionKey NVARCHAR(100)  NOT NULL,

        CreatedAt    DATETIME2(7)   NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt    DATETIME2(7)   NOT NULL DEFAULT SYSUTCDATETIME(),

        Status       TINYINT        NOT NULL DEFAULT (0),
        RetryCount   INT            NOT NULL DEFAULT (0),
        LastError    NVARCHAR(500)  NULL,

        CONSTRAINT PK_Outbox PRIMARY KEY NONCLUSTERED (Id)
    );

    CREATE CLUSTERED INDEX CX_Outbox_CreatedAt
        ON [HomeBudget.Accounting].dbo.OutboxAccountPayments (CreatedAt);

    CREATE NONCLUSTERED INDEX IX_Outbox_Status_CreatedAt
        ON [HomeBudget.Accounting].dbo.OutboxAccountPayments (Status, CreatedAt)
        INCLUDE (AggregateId, PartitionKey, EventType);
END
GO
