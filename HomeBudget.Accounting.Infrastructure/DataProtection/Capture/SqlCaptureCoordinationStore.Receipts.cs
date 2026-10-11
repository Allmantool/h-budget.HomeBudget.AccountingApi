using System;
using System.Threading;
using System.Threading.Tasks;

using Dapper;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed partial class SqlCaptureCoordinationStore
    {
        public async Task RecordReceiptAsync(
            MaintenanceEpoch epoch,
            CaptureComponentReceipt receipt,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DECLARE @LockResult INT;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @LockResource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 30000;
                IF @LockResult < 0
                    THROW 51023, 'Unable to acquire component receipt fence.', 1;

                DECLARE @Previous BIGINT =
                (
                    SELECT MAX(ProtectionSequence)
                      FROM dbo.ComponentReceipts
                     WHERE ComponentName = @ComponentName
                       AND ReceiptKind = @ReceiptKind
                );
                IF @Previous IS NOT NULL AND @ProtectionSequence < @Previous
                    THROW 51024, 'Component receipt sequence must be monotonic.', 1;

                INSERT INTO dbo.ComponentReceipts
                    (ReceiptId, OperationId, ComponentName, ReceiptKind,
                     ProtectionSequence, WatermarkJson, CapturedUtc)
                SELECT NEWID(), @OperationId, @ComponentName, @ReceiptKind,
                       @ProtectionSequence, @WatermarkJson, @CapturedUtc
                  FROM dbo.MaintenanceEpochs
                 WHERE EpochId = @EpochId
                   AND OperationId = @OperationId
                   AND State = N'closed';
                IF @@ROWCOUNT <> 1
                    THROW 51025, 'Maintenance epoch is no longer active.', 1;
                COMMIT TRANSACTION;";
            var parameters = new
            {
                epoch.EpochId,
                epoch.OperationId,
                receipt.ComponentName,
                receipt.ReceiptKind,
                receipt.ProtectionSequence,
                receipt.WatermarkJson,
                CapturedUtc = receipt.CommittedUtc.UtcDateTime,
                LockResource = LockResource(
                    "receipt",
                    $"{receipt.ComponentName}:{receipt.ReceiptKind}")
            };
            await using var connection = CreateConnection();
            await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        }

        public async Task RecordPendingWorkAsync(
            MaintenanceEpoch epoch,
            PendingWorkDisposition disposition,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO dbo.CapturePendingWorkDispositions
                    (EpochId, Disposition, PendingCount, PendingBytes,
                     HighestAcceptanceSequence, WatermarkJson, RecordedUtc)
                SELECT @EpochId, @Disposition, @Count, @Bytes,
                       @HighestAcceptanceSequence, @WatermarkJson, SYSUTCDATETIME()
                  FROM dbo.MaintenanceEpochs
                 WHERE EpochId = @EpochId
                   AND OperationId = @OperationId
                   AND State = N'closed';
                IF @@ROWCOUNT <> 1
                    THROW 51025, 'Maintenance epoch is no longer active.', 1;";
            await using var connection = CreateConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    epoch.EpochId,
                    epoch.OperationId,
                    disposition.Disposition,
                    disposition.Count,
                    disposition.Bytes,
                    disposition.HighestAcceptanceSequence,
                    disposition.WatermarkJson
                },
                cancellationToken: cancellationToken));
        }
    }
}
