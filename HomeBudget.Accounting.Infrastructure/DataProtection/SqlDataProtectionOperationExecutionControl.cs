using System;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class SqlDataProtectionOperationExecutionControl(
        string connectionString) : IDataProtectionOperationExecutionControl
    {
        private readonly string _connectionString = BuildConnectionString(connectionString);

        public async Task<bool> IsCancellationRequestedAsync(
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(fenceContext);
            const string sql = @"
                SELECT CAST(CASE WHEN CancellationRequested = 1 THEN 1 ELSE 0 END AS bit)
                FROM dbo.DataProtectionOperations
                WHERE OperationId = @OperationId
                  AND ScopeKey = @ScopeKey;";
            await using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleAsync<bool>(new CommandDefinition(
                sql,
                new { fenceContext.OperationId, fenceContext.ScopeKey },
                cancellationToken: cancellationToken));
        }

        public async Task SetStageAsync(
            DataProtectionOperationFenceContext fenceContext,
            string stage,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(fenceContext);
            ArgumentException.ThrowIfNullOrWhiteSpace(stage);
            if (stage.Length > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(stage), "Operation stage exceeds its durable bound.");
            }

            const string sql = @"
                UPDATE dbo.DataProtectionOperations
                   SET Stage = @Stage, UpdatedUtc = SYSUTCDATETIME()
                 WHERE OperationId = @OperationId
                   AND ScopeKey = @ScopeKey
                   AND EXISTS
                   (
                       SELECT 1
                         FROM dbo.DataProtectionLeases
                        WHERE LeaseId = @LeaseId
                          AND OperationId = @OperationId
                          AND ScopeKey = @ScopeKey
                          AND OwnerId = @OwnerId
                          AND FenceToken = @FenceToken
                          AND ExpiresUtc > SYSUTCDATETIME()
                   );
                SELECT @@ROWCOUNT;";
            await using var connection = new SqlConnection(_connectionString);
            var changed = await connection.QuerySingleAsync<int>(new CommandDefinition(
                sql,
                new
                {
                    fenceContext.OperationId,
                    fenceContext.LeaseId,
                    fenceContext.ScopeKey,
                    fenceContext.OwnerId,
                    fenceContext.FenceToken,
                    Stage = stage
                },
                cancellationToken: cancellationToken));
            if (changed != 1)
            {
                throw new InvalidOperationException("Data-protection operation stage update lost its lease fence.");
            }
        }

        private static string BuildConnectionString(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            return new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            }.ConnectionString;
        }
    }
}
