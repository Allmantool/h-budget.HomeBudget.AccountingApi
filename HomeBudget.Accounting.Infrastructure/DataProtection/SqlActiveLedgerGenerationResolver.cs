using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class SqlActiveLedgerGenerationResolver(
        string connectionString,
        LedgerGenerationOptions options) : IActiveLedgerGenerationResolver
    {
        public async Task<LedgerGenerationScope> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT LedgerId, GenerationId, GenerationNumber, GenerationToken
                FROM dbo.ActiveLedgerGenerations
                WHERE EnvironmentId = @EnvironmentId
                  AND LedgerId = @LedgerId;";
            command.Parameters.Add("@EnvironmentId", SqlDbType.NVarChar, 128).Value = options.EnvironmentId;
            command.Parameters.Add("@LedgerId", SqlDbType.NVarChar, 128).Value = options.LedgerId;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The configured active ledger generation was not found.");
            }

            return new LedgerGenerationScope(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                $"\"{reader.GetString(3)}\"")
            {
                EnvironmentId = options.EnvironmentId
            };
        }
    }
}
