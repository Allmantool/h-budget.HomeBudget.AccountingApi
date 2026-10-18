using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal static class DataProtectionOperationSnapshotReader
    {
        public static async Task<IReadOnlyCollection<string>> ReadAsync(
            TestContainersService testContainers,
            CancellationToken cancellationToken)
        {
            await using var connection = await DataProtectionControlPlaneTestDatabase.OpenAsync(
                testContainers,
                cancellationToken,
                DataProtectionControlPlaneTestDatabase.OperationsTable);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT OperationId, OperationKind, ScopeKind, ScopeKey, EnvironmentId,
                       LedgerId, GenerationId, PlanFingerprint, State, Stage,
                       CancellationRequested, CreatedUtc, UpdatedUtc, RowVersion
                  FROM dbo.DataProtectionOperations
                 ORDER BY OperationId;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<string>();

            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(string.Join(
                    "|",
                    Enumerable.Range(0, reader.FieldCount).Select(index => ReadValue(reader, index))));
            }

            return rows;
        }

        private static string ReadValue(SqlDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return "<null>";
            }

            return reader.GetValue(index) is byte[] bytes
                ? Convert.ToHexString(bytes)
                : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);
        }
    }
}
