-- evolve-tx-off
USE [HomeBudget.DataProtection];
GO

IF OBJECT_ID(N'dbo.DataProtectionPlans', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataProtectionPlans
    (
        PlanId UNIQUEIDENTIFIER NOT NULL,
        OperationKind NVARCHAR(64) NOT NULL,
        ScopeKind NVARCHAR(32) NOT NULL,
        ScopeKey NVARCHAR(256) NOT NULL,
        EnvironmentId NVARCHAR(128) NOT NULL,
        LedgerId NVARCHAR(128) NULL,
        GenerationId NVARCHAR(128) NULL,
        PlanFingerprint CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorId NVARCHAR(256) NOT NULL,
        ConfirmationTokenHash CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedUtc DATETIME2(7) NOT NULL,
        ExpiresUtc DATETIME2(7) NOT NULL,
        ConsumedUtc DATETIME2(7) NULL,
        ConsumedByOperationId UNIQUEIDENTIFIER NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_DataProtectionPlans PRIMARY KEY (PlanId),
        CONSTRAINT FK_DataProtectionPlans_ConsumedOperation FOREIGN KEY (ConsumedByOperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT UX_DataProtectionPlans_ConfirmationTokenHash UNIQUE (ConfirmationTokenHash),
        CONSTRAINT CK_DataProtectionPlans_OperationKind CHECK
            (OperationKind IN
                (N'backup', N'export', N'import', N'replace', N'restore', N'pitr',
                 N'projection-rebuild', N'ledger-reset', N'environment-reset')),
        CONSTRAINT CK_DataProtectionPlans_ScopeKind CHECK
            (ScopeKind IN (N'ledger', N'environment')),
        CONSTRAINT CK_DataProtectionPlans_LedgerScope CHECK
            ((ScopeKind = N'ledger' AND LedgerId IS NOT NULL AND GenerationId IS NOT NULL)
             OR (ScopeKind = N'environment' AND LedgerId IS NULL AND GenerationId IS NULL)),
        CONSTRAINT CK_DataProtectionPlans_PlanFingerprint CHECK
            (LEN(PlanFingerprint) = 64 AND PlanFingerprint NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_DataProtectionPlans_TokenHash CHECK
            (LEN(ConfirmationTokenHash) = 64 AND ConfirmationTokenHash NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_DataProtectionPlans_Expiry CHECK (ExpiresUtc > CreatedUtc),
        CONSTRAINT CK_DataProtectionPlans_Consumption CHECK
            ((ConsumedUtc IS NULL AND ConsumedByOperationId IS NULL)
             OR (ConsumedUtc IS NOT NULL AND ConsumedByOperationId IS NOT NULL
                 AND ConsumedUtc >= CreatedUtc))
    );

    CREATE INDEX IX_DataProtectionPlans_ActorExpiry
        ON dbo.DataProtectionPlans (ActorId, ExpiresUtc)
        INCLUDE (OperationKind, ScopeKey, ConsumedUtc);
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_DataProtectionPlans_ImmutableBinding
ON dbo.DataProtectionPlans
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
          FROM inserted AS current_row
          INNER JOIN deleted AS previous_row ON previous_row.PlanId = current_row.PlanId
         WHERE current_row.OperationKind <> previous_row.OperationKind
            OR current_row.ScopeKind <> previous_row.ScopeKind
            OR current_row.ScopeKey <> previous_row.ScopeKey
            OR current_row.EnvironmentId <> previous_row.EnvironmentId
            OR ISNULL(current_row.LedgerId, N'') <> ISNULL(previous_row.LedgerId, N'')
            OR ISNULL(current_row.GenerationId, N'') <> ISNULL(previous_row.GenerationId, N'')
            OR current_row.PlanFingerprint <> previous_row.PlanFingerprint
            OR current_row.ActorId <> previous_row.ActorId
            OR current_row.ConfirmationTokenHash <> previous_row.ConfirmationTokenHash
            OR current_row.CreatedUtc <> previous_row.CreatedUtc
            OR current_row.ExpiresUtc <> previous_row.ExpiresUtc
            OR (previous_row.ConsumedUtc IS NOT NULL
                AND (current_row.ConsumedUtc <> previous_row.ConsumedUtc
                     OR current_row.ConsumedByOperationId <> previous_row.ConsumedByOperationId))
    )
    BEGIN
        THROW 51002, N'Data-protection plan binding and consumption are immutable.', 1;
    END;
END;
GO

USE [HomeBudget.Accounting];
GO
