-- evolve-tx-off
USE [HomeBudget.DataProtection];
GO

IF OBJECT_ID(N'dbo.MaintenanceEpochs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaintenanceEpochs
    (
        EpochId UNIQUEIDENTIFIER NOT NULL,
        EpochNumber BIGINT IDENTITY(1, 1) NOT NULL,
        OperationId UNIQUEIDENTIFIER NOT NULL,
        ScopeKey NVARCHAR(256) NOT NULL,
        GenerationId NVARCHAR(128) NOT NULL,
        State NVARCHAR(16) NOT NULL,
        AdmissionClosedUtc DATETIME2(7) NOT NULL,
        ReleasedUtc DATETIME2(7) NULL,
        CONSTRAINT PK_MaintenanceEpochs PRIMARY KEY (EpochId),
        CONSTRAINT UX_MaintenanceEpochs_EpochNumber UNIQUE (EpochNumber),
        CONSTRAINT FK_MaintenanceEpochs_Operation FOREIGN KEY (OperationId)
            REFERENCES dbo.DataProtectionOperations (OperationId),
        CONSTRAINT CK_MaintenanceEpochs_State CHECK
            (State IN (N'closed', N'released', N'failed')),
        CONSTRAINT CK_MaintenanceEpochs_Release CHECK
            ((State = N'closed' AND ReleasedUtc IS NULL)
             OR (State IN (N'released', N'failed') AND ReleasedUtc IS NOT NULL))
    );

    CREATE UNIQUE INDEX UX_MaintenanceEpochs_ActiveScope
        ON dbo.MaintenanceEpochs (ScopeKey)
        WHERE State = N'closed';

    CREATE INDEX IX_MaintenanceEpochs_ScopeGeneration
        ON dbo.MaintenanceEpochs (ScopeKey, GenerationId, State, EpochNumber);
END;
GO

IF OBJECT_ID(N'dbo.CapturePendingWorkDispositions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CapturePendingWorkDispositions
    (
        EpochId UNIQUEIDENTIFIER NOT NULL,
        Disposition NVARCHAR(16) NOT NULL,
        PendingCount BIGINT NOT NULL,
        PendingBytes BIGINT NOT NULL,
        HighestAcceptanceSequence BIGINT NOT NULL,
        WatermarkJson NVARCHAR(MAX) NOT NULL,
        RecordedUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_CapturePendingWorkDispositions PRIMARY KEY (EpochId),
        CONSTRAINT FK_CapturePendingWorkDispositions_Epoch FOREIGN KEY (EpochId)
            REFERENCES dbo.MaintenanceEpochs (EpochId),
        CONSTRAINT CK_CapturePendingWorkDispositions_Disposition CHECK
            (Disposition IN (N'drained', N'included')),
        CONSTRAINT CK_CapturePendingWorkDispositions_Counts CHECK
            (PendingCount >= 0 AND PendingBytes >= 0 AND HighestAcceptanceSequence >= 0),
        CONSTRAINT CK_CapturePendingWorkDispositions_Json CHECK (ISJSON(WatermarkJson) = 1),
        CONSTRAINT CK_CapturePendingWorkDispositions_Drained CHECK
            (Disposition <> N'drained' OR (PendingCount = 0 AND PendingBytes = 0))
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ComponentReceipts')
      AND name = N'IX_ComponentReceipts_Monotonic'
)
BEGIN
    CREATE INDEX IX_ComponentReceipts_Monotonic
        ON dbo.ComponentReceipts
            (ComponentName, ReceiptKind, ProtectionSequence DESC, CapturedUtc DESC);
END;
GO

IF COL_LENGTH(N'dbo.RecoveryManifests', N'FailureClassification') IS NULL
BEGIN
    ALTER TABLE dbo.RecoveryManifests
        ADD FailureClassification NVARCHAR(128) NULL;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_MaintenanceEpochs_ImmutableBinding
ON dbo.MaintenanceEpochs
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS current_row
        INNER JOIN deleted AS previous_row
            ON previous_row.EpochId = current_row.EpochId
        WHERE current_row.EpochNumber <> previous_row.EpochNumber
           OR current_row.OperationId <> previous_row.OperationId
           OR current_row.ScopeKey <> previous_row.ScopeKey
           OR current_row.GenerationId <> previous_row.GenerationId
           OR current_row.AdmissionClosedUtc <> previous_row.AdmissionClosedUtc
    )
    BEGIN
        THROW 51020, N'Maintenance epoch binding is immutable.', 1;
    END;
END;
GO

USE [HomeBudget.Accounting];
GO

