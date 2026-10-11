-- evolve-tx-off
USE [HomeBudget.DataProtection];
GO

IF OBJECT_ID(N'dbo.RecoveryTriggerStates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecoveryTriggerStates
    (
        EnvironmentId NVARCHAR(128) NOT NULL,
        LedgerId NVARCHAR(128) NOT NULL,
        GenerationId NVARCHAR(128) NOT NULL,
        ScopeKey NVARCHAR(256) NOT NULL,
        PendingRecordCount BIGINT NOT NULL,
        PendingByteCount BIGINT NOT NULL,
        FirstPendingUtc DATETIME2(7) NULL,
        LastChangeUtc DATETIME2(7) NULL,
        LastScheduledUtc DATETIME2(7) NULL,
        LastTriggeredUtc DATETIME2(7) NULL,
        LastTriggerReason NVARCHAR(512) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_RecoveryTriggerStates PRIMARY KEY
            (EnvironmentId, ScopeKey, GenerationId),
        CONSTRAINT CK_RecoveryTriggerStates_Counts CHECK
            (PendingRecordCount >= 0 AND PendingByteCount >= 0),
        CONSTRAINT CK_RecoveryTriggerStates_PendingWindow CHECK
            ((PendingRecordCount = 0 AND PendingByteCount = 0 AND FirstPendingUtc IS NULL)
             OR ((PendingRecordCount > 0 OR PendingByteCount > 0) AND FirstPendingUtc IS NOT NULL)),
        CONSTRAINT CK_RecoveryTriggerStates_ChangeWindow CHECK
            (FirstPendingUtc IS NULL OR LastChangeUtc IS NULL OR LastChangeUtc >= FirstPendingUtc)
    );

    CREATE INDEX IX_RecoveryTriggerStates_Scheduled
        ON dbo.RecoveryTriggerStates (LastScheduledUtc, EnvironmentId, ScopeKey);
END;
GO

IF OBJECT_ID(N'dbo.RecoveryTriggerDispatches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecoveryTriggerDispatches
    (
        OperationId UNIQUEIDENTIFIER NOT NULL,
        TriggerKind NVARCHAR(32) NOT NULL,
        TriggerReason NVARCHAR(512) NOT NULL,
        SourceIdentity NVARCHAR(256) NULL,
        RecordCount BIGINT NOT NULL,
        ByteCount BIGINT NOT NULL,
        KeyReference NVARCHAR(512) NOT NULL,
        RequiredComponentsJson NVARCHAR(MAX) NOT NULL,
        CreatedUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_RecoveryTriggerDispatches PRIMARY KEY (OperationId),
        CONSTRAINT FK_RecoveryTriggerDispatches_Operation FOREIGN KEY (OperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT CK_RecoveryTriggerDispatches_Kind CHECK
            (TriggerKind IN
                (N'scheduled', N'on-demand', N'named-bulk', N'threshold',
                 N'pre-operation', N'post-reconciliation')),
        CONSTRAINT CK_RecoveryTriggerDispatches_Counts CHECK
            (RecordCount >= 0 AND ByteCount >= 0),
        CONSTRAINT CK_RecoveryTriggerDispatches_ComponentsJson CHECK
            (ISJSON(RequiredComponentsJson) = 1)
    );

    CREATE INDEX IX_RecoveryTriggerDispatches_Created
        ON dbo.RecoveryTriggerDispatches (CreatedUtc, TriggerKind);
END;
GO

USE [HomeBudget.Accounting];
GO

