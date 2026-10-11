-- evolve-tx-off
USE master;
GO

IF DB_ID(N'HomeBudget.DataProtection') IS NULL
BEGIN
    CREATE DATABASE [HomeBudget.DataProtection];
END;
GO

USE [HomeBudget.DataProtection];
GO

IF OBJECT_ID(N'dbo.DataProtectionOperations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataProtectionOperations
    (
        OperationId UNIQUEIDENTIFIER NOT NULL,
        OperationKind NVARCHAR(64) NOT NULL,
        ScopeKind NVARCHAR(32) NOT NULL,
        ScopeKey NVARCHAR(256) NOT NULL,
        EnvironmentId NVARCHAR(128) NOT NULL,
        LedgerId NVARCHAR(128) NULL,
        GenerationId NVARCHAR(128) NULL,
        PlanFingerprint CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        State NVARCHAR(32) NOT NULL,
        Stage NVARCHAR(128) NULL,
        CancellationRequested BIT NOT NULL
            CONSTRAINT DF_DataProtectionOperations_CancellationRequested DEFAULT (0),
        CreatedUtc DATETIME2(7) NOT NULL,
        UpdatedUtc DATETIME2(7) NOT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_DataProtectionOperations PRIMARY KEY (OperationId),
        CONSTRAINT CK_DataProtectionOperations_OperationKind CHECK
            (OperationKind IN
                (N'backup', N'export', N'import', N'replace', N'restore', N'pitr',
                 N'projection-rebuild', N'ledger-reset', N'environment-reset')),
        CONSTRAINT CK_DataProtectionOperations_ScopeKind CHECK
            (ScopeKind IN (N'ledger', N'environment')),
        CONSTRAINT CK_DataProtectionOperations_LedgerScope CHECK
            ((ScopeKind = N'ledger' AND LedgerId IS NOT NULL AND GenerationId IS NOT NULL)
             OR ScopeKind = N'environment'),
        CONSTRAINT CK_DataProtectionOperations_PlanFingerprint CHECK
            (LEN(PlanFingerprint) = 64
             AND PlanFingerprint NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_DataProtectionOperations_State CHECK
            (State IN
                (N'queued', N'validating', N'running', N'awaiting-reconciliation',
                 N'succeeded', N'failed', N'cancelled')),
        CONSTRAINT CK_DataProtectionOperations_Timestamps CHECK (UpdatedUtc >= CreatedUtc)
    );

    CREATE INDEX IX_DataProtectionOperations_ScopeState
        ON dbo.DataProtectionOperations (ScopeKey, State, UpdatedUtc);
END;
GO

IF OBJECT_ID(N'dbo.DataProtectionLeases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataProtectionLeases
    (
        LeaseId UNIQUEIDENTIFIER NOT NULL,
        OperationId UNIQUEIDENTIFIER NOT NULL,
        ScopeKey NVARCHAR(256) NOT NULL,
        OwnerId NVARCHAR(128) NOT NULL,
        FenceToken BIGINT IDENTITY(1, 1) NOT NULL,
        AcquiredUtc DATETIME2(7) NOT NULL,
        RenewedUtc DATETIME2(7) NOT NULL,
        ExpiresUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_DataProtectionLeases PRIMARY KEY (LeaseId),
        CONSTRAINT FK_DataProtectionLeases_Operation FOREIGN KEY (OperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT UX_DataProtectionLeases_ScopeKey UNIQUE (ScopeKey),
        CONSTRAINT UX_DataProtectionLeases_FenceToken UNIQUE (FenceToken),
        CONSTRAINT CK_DataProtectionLeases_Timestamps CHECK
            (RenewedUtc >= AcquiredUtc AND ExpiresUtc > RenewedUtc)
    );
END;
GO

IF OBJECT_ID(N'dbo.ActiveLedgerGenerations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ActiveLedgerGenerations
    (
        EnvironmentId NVARCHAR(128) NOT NULL,
        LedgerId NVARCHAR(128) NOT NULL,
        GenerationId NVARCHAR(128) NOT NULL,
        PreviousGenerationId NVARCHAR(128) NULL,
        GenerationNumber BIGINT NOT NULL,
        ActivatedByOperationId UNIQUEIDENTIFIER NULL,
        ActivatedUtc DATETIME2(7) NOT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_ActiveLedgerGenerations PRIMARY KEY (EnvironmentId, LedgerId),
        CONSTRAINT FK_ActiveLedgerGenerations_Operation FOREIGN KEY (ActivatedByOperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT UX_ActiveLedgerGenerations_Generation UNIQUE
            (EnvironmentId, LedgerId, GenerationId),
        CONSTRAINT CK_ActiveLedgerGenerations_Number CHECK (GenerationNumber >= 0),
        CONSTRAINT CK_ActiveLedgerGenerations_Previous CHECK
            (PreviousGenerationId IS NULL OR PreviousGenerationId <> GenerationId)
    );
END;
GO

IF OBJECT_ID(N'dbo.ComponentReceipts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComponentReceipts
    (
        ReceiptId UNIQUEIDENTIFIER NOT NULL,
        OperationId UNIQUEIDENTIFIER NOT NULL,
        ComponentName NVARCHAR(64) NOT NULL,
        ReceiptKind NVARCHAR(64) NOT NULL,
        ProtectionSequence BIGINT NULL,
        WatermarkJson NVARCHAR(MAX) NOT NULL,
        CapturedUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_ComponentReceipts PRIMARY KEY (ReceiptId),
        CONSTRAINT FK_ComponentReceipts_Operation FOREIGN KEY (OperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT UX_ComponentReceipts_OperationComponentKind UNIQUE
            (OperationId, ComponentName, ReceiptKind),
        CONSTRAINT CK_ComponentReceipts_ProtectionSequence CHECK
            (ProtectionSequence IS NULL OR ProtectionSequence >= 0),
        CONSTRAINT CK_ComponentReceipts_WatermarkJson CHECK (ISJSON(WatermarkJson) = 1)
    );
END;
GO

IF OBJECT_ID(N'dbo.ImportOutcomes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImportOutcomes
    (
        ImportOutcomeId UNIQUEIDENTIFIER NOT NULL,
        OperationId UNIQUEIDENTIFIER NOT NULL,
        EntityType NVARCHAR(64) NOT NULL,
        SourceEntityId NVARCHAR(256) NOT NULL,
        TargetEntityId NVARCHAR(256) NULL,
        IdempotencyKey NVARCHAR(256) NOT NULL,
        Fingerprint CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        State NVARCHAR(32) NOT NULL,
        ErrorClassification NVARCHAR(128) NULL,
        CreatedUtc DATETIME2(7) NOT NULL,
        UpdatedUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_ImportOutcomes PRIMARY KEY (ImportOutcomeId),
        CONSTRAINT FK_ImportOutcomes_Operation FOREIGN KEY (OperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT UX_ImportOutcomes_SourceIdentity UNIQUE
            (OperationId, EntityType, SourceEntityId),
        CONSTRAINT UX_ImportOutcomes_IdempotencyKey UNIQUE (OperationId, IdempotencyKey),
        CONSTRAINT CK_ImportOutcomes_Fingerprint CHECK
            (LEN(Fingerprint) = 64 AND Fingerprint NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_ImportOutcomes_State CHECK
            (State IN
                (N'pending', N'validated', N'accepted', N'persisted', N'projected',
                 N'reconciled', N'failed', N'conflict')),
        CONSTRAINT CK_ImportOutcomes_Timestamps CHECK (UpdatedUtc >= CreatedUtc)
    );
END;
GO

IF OBJECT_ID(N'dbo.RecoveryManifests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecoveryManifests
    (
        RecoverySetId UNIQUEIDENTIFIER NOT NULL,
        OperationId UNIQUEIDENTIFIER NOT NULL,
        EnvironmentId NVARCHAR(128) NOT NULL,
        LedgerScope NVARCHAR(256) NOT NULL,
        SourceGenerationId NVARCHAR(128) NOT NULL,
        SchemaVersion NVARCHAR(64) NOT NULL,
        CaptureStartedUtc DATETIME2(7) NOT NULL,
        CommonCutoffUtc DATETIME2(7) NOT NULL,
        ProtectedUtc DATETIME2(7) NULL,
        KeyReference NVARCHAR(512) NOT NULL,
        ManifestChecksum CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ManifestJson NVARCHAR(MAX) NOT NULL,
        VerificationStatus NVARCHAR(32) NOT NULL,
        PublishedUtc DATETIME2(7) NULL,
        CONSTRAINT PK_RecoveryManifests PRIMARY KEY (RecoverySetId),
        CONSTRAINT FK_RecoveryManifests_Operation FOREIGN KEY (OperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT UX_RecoveryManifests_Operation UNIQUE (OperationId),
        CONSTRAINT CK_RecoveryManifests_Checksum CHECK
            (LEN(ManifestChecksum) = 64 AND ManifestChecksum NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_RecoveryManifests_Json CHECK (ISJSON(ManifestJson) = 1),
        CONSTRAINT CK_RecoveryManifests_VerificationStatus CHECK
            (VerificationStatus IN (N'pending', N'verified', N'failed')),
        CONSTRAINT CK_RecoveryManifests_Cutoff CHECK
            (CommonCutoffUtc >= CaptureStartedUtc),
        CONSTRAINT CK_RecoveryManifests_Publication CHECK
            (PublishedUtc IS NULL
             OR (VerificationStatus = N'verified' AND ProtectedUtc IS NOT NULL))
    );

    CREATE INDEX IX_RecoveryManifests_ProtectedCutoff
        ON dbo.RecoveryManifests (EnvironmentId, ProtectedUtc, CommonCutoffUtc)
        WHERE ProtectedUtc IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.ProtectedAuditEntries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProtectedAuditEntries
    (
        AuditId UNIQUEIDENTIFIER NOT NULL,
        OperationId UNIQUEIDENTIFIER NOT NULL,
        ActorId NVARCHAR(256) NOT NULL,
        EventType NVARCHAR(128) NOT NULL,
        ScopeKey NVARCHAR(256) NOT NULL,
        TargetEnvironmentId NVARCHAR(128) NULL,
        TargetLedgerId NVARCHAR(128) NULL,
        TargetGenerationId NVARCHAR(128) NULL,
        SourceChecksum CHAR(64) COLLATE Latin1_General_100_BIN2 NULL,
        RecoverySetId UNIQUEIDENTIFIER NULL,
        RequestedCutoffUtc DATETIME2(7) NULL,
        ActualCutoffUtc DATETIME2(7) NULL,
        PlanFingerprint CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ConfirmationFingerprint CHAR(64) COLLATE Latin1_General_100_BIN2 NULL,
        Result NVARCHAR(64) NOT NULL,
        ErrorClassification NVARCHAR(128) NULL,
        OccurredUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_ProtectedAuditEntries PRIMARY KEY (AuditId),
        CONSTRAINT FK_ProtectedAuditEntries_Operation FOREIGN KEY (OperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT CK_ProtectedAuditEntries_SourceChecksum CHECK
            (SourceChecksum IS NULL
             OR (LEN(SourceChecksum) = 64 AND SourceChecksum NOT LIKE '%[^0-9a-f]%')),
        CONSTRAINT CK_ProtectedAuditEntries_PlanFingerprint CHECK
            (LEN(PlanFingerprint) = 64 AND PlanFingerprint NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_ProtectedAuditEntries_ConfirmationFingerprint CHECK
            (ConfirmationFingerprint IS NULL
             OR (LEN(ConfirmationFingerprint) = 64
                 AND ConfirmationFingerprint NOT LIKE '%[^0-9a-f]%')),
        CONSTRAINT CK_ProtectedAuditEntries_Cutoff CHECK
            (RequestedCutoffUtc IS NULL OR ActualCutoffUtc IS NULL
             OR ActualCutoffUtc <= RequestedCutoffUtc)
    );

    CREATE INDEX IX_ProtectedAuditEntries_OperationOccurred
        ON dbo.ProtectedAuditEntries (OperationId, OccurredUtc);
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_DataProtectionOperations_ImmutableBinding
ON dbo.DataProtectionOperations
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS current_row
        INNER JOIN deleted AS previous_row
            ON previous_row.OperationId = current_row.OperationId
        WHERE current_row.OperationKind <> previous_row.OperationKind
           OR current_row.ScopeKind <> previous_row.ScopeKind
           OR current_row.ScopeKey <> previous_row.ScopeKey
           OR current_row.EnvironmentId <> previous_row.EnvironmentId
           OR ISNULL(current_row.LedgerId, N'') <> ISNULL(previous_row.LedgerId, N'')
           OR ISNULL(current_row.GenerationId, N'') <> ISNULL(previous_row.GenerationId, N'')
           OR current_row.PlanFingerprint <> previous_row.PlanFingerprint
           OR current_row.CreatedUtc <> previous_row.CreatedUtc
    )
    BEGIN
        THROW 51000, N'Data-protection operation identity and plan binding are immutable.', 1;
    END;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_ProtectedAuditEntries_AppendOnly
ON dbo.ProtectedAuditEntries
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51001, N'Protected audit entries are append-only.', 1;
END;
GO

USE [HomeBudget.Accounting];
GO
