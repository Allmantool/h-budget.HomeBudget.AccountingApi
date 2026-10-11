-- evolve-tx-off
USE [HomeBudget.DataProtection];
GO

IF OBJECT_ID(N'dbo.PortabilityImportRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PortabilityImportRuns
    (
        OperationId UNIQUEIDENTIFIER NOT NULL,
        ExportId UNIQUEIDENTIFIER NOT NULL,
        PackageSha256 CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FormatVersion NVARCHAR(32) NOT NULL,
        Completeness NVARCHAR(16) NOT NULL,
        ImportMode NVARCHAR(32) NOT NULL,
        TargetEnvironmentId NVARCHAR(128) NOT NULL,
        TargetLedgerId NVARCHAR(128) NOT NULL,
        TargetGenerationId NVARCHAR(128) NOT NULL,
        TargetGenerationNumber BIGINT NOT NULL,
        PreOperationRecoverySetId UNIQUEIDENTIFIER NULL,
        EntityCount BIGINT NOT NULL,
        State NVARCHAR(32) NOT NULL,
        CreatedUtc DATETIME2(7) NOT NULL,
        UpdatedUtc DATETIME2(7) NOT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_PortabilityImportRuns PRIMARY KEY (OperationId),
        CONSTRAINT FK_PortabilityImportRuns_Operation FOREIGN KEY (OperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT UX_PortabilityImportRuns_ExportTarget UNIQUE
            (ExportId, TargetEnvironmentId, TargetLedgerId, TargetGenerationId),
        CONSTRAINT CK_PortabilityImportRuns_PackageSha256 CHECK
            (LEN(PackageSha256) = 64 AND PackageSha256 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_PortabilityImportRuns_Completeness CHECK
            (Completeness IN (N'full', N'partial')),
        CONSTRAINT CK_PortabilityImportRuns_Mode CHECK
            (ImportMode IN (N'empty-target', N'resume', N'replace')),
        CONSTRAINT CK_PortabilityImportRuns_TargetGenerationNumber CHECK
            (TargetGenerationNumber >= 0),
        CONSTRAINT CK_PortabilityImportRuns_EntityCount CHECK (EntityCount >= 0),
        CONSTRAINT CK_PortabilityImportRuns_State CHECK
            (State IN
                (N'validated', N'journaled', N'executing', N'awaiting-settlement',
                 N'awaiting-reconciliation', N'succeeded', N'failed', N'cancelled')),
        CONSTRAINT CK_PortabilityImportRuns_Timestamps CHECK (UpdatedUtc >= CreatedUtc)
    );
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_PortabilityImportRuns_ImmutableBinding
ON dbo.PortabilityImportRuns
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS currentRow
        INNER JOIN deleted AS previousRow ON previousRow.OperationId = currentRow.OperationId
        WHERE currentRow.ExportId <> previousRow.ExportId
           OR currentRow.PackageSha256 <> previousRow.PackageSha256
           OR currentRow.FormatVersion <> previousRow.FormatVersion
           OR currentRow.Completeness <> previousRow.Completeness
           OR currentRow.ImportMode <> previousRow.ImportMode
           OR currentRow.TargetEnvironmentId <> previousRow.TargetEnvironmentId
           OR currentRow.TargetLedgerId <> previousRow.TargetLedgerId
           OR currentRow.TargetGenerationId <> previousRow.TargetGenerationId
           OR currentRow.TargetGenerationNumber <> previousRow.TargetGenerationNumber
           OR ISNULL(CONVERT(NVARCHAR(36), currentRow.PreOperationRecoverySetId), N'')
                <> ISNULL(CONVERT(NVARCHAR(36), previousRow.PreOperationRecoverySetId), N'')
           OR currentRow.EntityCount <> previousRow.EntityCount
           OR currentRow.CreatedUtc <> previousRow.CreatedUtc
    )
    BEGIN
        THROW 51020, 'Portability import run binding is immutable.', 1;
    END;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_ImportOutcomes_ImmutableBinding
ON dbo.ImportOutcomes
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS currentRow
        INNER JOIN deleted AS previousRow
            ON previousRow.ImportOutcomeId = currentRow.ImportOutcomeId
        WHERE currentRow.OperationId <> previousRow.OperationId
           OR currentRow.EntityType <> previousRow.EntityType
           OR currentRow.SourceEntityId <> previousRow.SourceEntityId
           OR ISNULL(currentRow.TargetEntityId, N'') <> ISNULL(previousRow.TargetEntityId, N'')
           OR currentRow.IdempotencyKey <> previousRow.IdempotencyKey
           OR currentRow.Fingerprint <> previousRow.Fingerprint
           OR currentRow.CreatedUtc <> previousRow.CreatedUtc
    )
    BEGIN
        THROW 51021, 'Portability import entity binding is immutable.', 1;
    END;
END;
GO
