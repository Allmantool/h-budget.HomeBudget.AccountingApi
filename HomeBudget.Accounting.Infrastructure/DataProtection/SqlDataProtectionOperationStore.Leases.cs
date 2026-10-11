using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed partial class SqlDataProtectionOperationStore
    {
        public async Task<DataProtectionLease> TryAcquireLeaseAsync(
            Guid operationId,
            string scopeKey,
            string ownerId,
            DateTime nowUtc,
            DateTime expiresUtc,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresUtc, nowUtc);

            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                DELETE FROM dbo.DataProtectionLeases
                 WHERE ScopeKey = @ScopeKey AND ExpiresUtc <= @NowUtc;

                DECLARE @LeaseId uniqueidentifier = NEWID();
                INSERT INTO dbo.DataProtectionLeases
                    (LeaseId, OperationId, ScopeKey, OwnerId, AcquiredUtc, RenewedUtc, ExpiresUtc)
                VALUES
                    (@LeaseId, @OperationId, @ScopeKey, @OwnerId, @NowUtc, @NowUtc, @ExpiresUtc);

                COMMIT TRANSACTION;

                SELECT LeaseId, OperationId, ScopeKey, OwnerId, FenceToken, ExpiresUtc
                  FROM dbo.DataProtectionLeases
                 WHERE LeaseId = @LeaseId;";
            await using var connection = CreateConnection();

            try
            {
                return await connection.QuerySingleAsync<DataProtectionLease>(new CommandDefinition(
                    sql,
                    new { OperationId = operationId, ScopeKey = scopeKey, OwnerId = ownerId, NowUtc = nowUtc, ExpiresUtc = expiresUtc },
                    cancellationToken: cancellationToken));
            }
            catch (SqlException exception) when (exception.Number is 2601 or 2627)
            {
                return null;
            }
        }

        public async Task<bool> RenewLeaseAsync(
            DataProtectionOperationFenceContext fenceContext,
            DateTime nowUtc,
            DateTime expiresUtc,
            CancellationToken cancellationToken)
        {
            ValidateFenceContext(fenceContext);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresUtc, nowUtc);
            const string sql = @"
                UPDATE dbo.DataProtectionLeases
                   SET RenewedUtc = @NowUtc, ExpiresUtc = @ExpiresUtc
                 WHERE LeaseId = @LeaseId
                   AND OperationId = @OperationId
                   AND ScopeKey = @ScopeKey
                   AND OwnerId = @OwnerId
                   AND FenceToken = @FenceToken
                   AND ExpiresUtc > @NowUtc;
                SELECT @@ROWCOUNT;";
            await using var connection = CreateConnection();
            var changed = await connection.QuerySingleAsync<int>(new CommandDefinition(
                sql,
                new
                {
                    fenceContext.LeaseId,
                    fenceContext.OperationId,
                    fenceContext.ScopeKey,
                    fenceContext.OwnerId,
                    fenceContext.FenceToken,
                    NowUtc = nowUtc,
                    ExpiresUtc = expiresUtc
                },
                cancellationToken: cancellationToken));
            return changed == 1;
        }

        public async Task ReleaseLeaseAsync(
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            ValidateFenceContext(fenceContext);
            const string sql = @"
                DELETE FROM dbo.DataProtectionLeases
                WHERE LeaseId = @LeaseId
                  AND OperationId = @OperationId
                  AND ScopeKey = @ScopeKey
                  AND OwnerId = @OwnerId
                  AND FenceToken = @FenceToken;";
            await using var connection = CreateConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                fenceContext,
                cancellationToken: cancellationToken));
        }

        public async Task<DataProtectionOperation> WaitForChangeAsync(
            Guid operationId,
            byte[] knownRowVersion,
            TimeSpan timeout,
            TimeSpan pollInterval,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(knownRowVersion);
            if (timeout <= TimeSpan.Zero || pollInterval <= TimeSpan.Zero || pollInterval > timeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Polling bounds must be positive and interval must not exceed timeout.");
            }

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = await GetAsync(operationId, cancellationToken);
                if (operation is null || !operation.RowVersion.SequenceEqual(knownRowVersion))
                {
                    return operation;
                }

                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(remaining < pollInterval ? remaining : pollInterval, cancellationToken);
            }

            return null;
        }
    }
}
