using System.Threading.Tasks;

using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class MsSqlDbContainerExtensions
    {
        public static async Task ResetContainersAsync(this MsSqlContainer container)
        {
            var builder = new SqlConnectionStringBuilder(container.GetConnectionString())
            {
                InitialCatalog = "HomeBudget.Accounting"
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = @"
                IF OBJECT_ID(N'dbo.PaymentInboxMessages', N'U') IS NOT NULL
                BEGIN
                    DELETE FROM dbo.PaymentInboxMessages;
                END;

                IF OBJECT_ID(N'dbo.OutboxAccountPayments', N'U') IS NOT NULL
                BEGIN
                    DELETE FROM dbo.OutboxAccountPayments;
                END;";

            await command.ExecuteNonQueryAsync();
        }
    }
}
