using System.Data;
using System.Threading.Tasks;

using Dapper;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.Data.SqlClients.MsSql
{
    public sealed class DapperWriteRepository(
        ISqlConnectionFactory sqlConnectionFactory,
        IOptions<DatabaseConnectionOptions> sqlOptions)
    : IBaseWriteRepository
    {
        public async Task<int> ExecuteAsync<T>(
            string sqlQuery,
            IDbTransaction dbTransaction = null)
            where T : IDbEntity
        {
            using var db = sqlConnectionFactory.Create();

            return await ExecuteAsync<T>(sqlQuery, dbTransaction);
        }

        public async Task<int> ExecuteAsync<T>(
            string sqlQuery,
            T parameters,
            IDbTransaction dbTransaction = null)
            where T : IDbEntity
        {
            using var db = sqlConnectionFactory.Create();

            return await db.ExecuteAsync(
                sqlQuery,
                parameters,
                transaction: dbTransaction,
                commandTimeout: sqlOptions.Value.SqlWriteCommandTimeoutSeconds);
        }

        public async Task<int> ExecuteAsync<T>(
            string sqlQuery,
            T[] parameters,
            IDbTransaction dbTransaction = null)
            where T : IDbEntity
        {
            using var db = sqlConnectionFactory.Create();

            return await db.ExecuteAsync(
                sqlQuery,
                parameters,
                transaction: dbTransaction,
                commandTimeout: sqlOptions.Value.SqlWriteCommandTimeoutSeconds);
        }

        public async Task<int> ExecuteAsync(
            string sqlQuery,
            DataTable dt,
            string mapToDbType,
            IDbTransaction dbTransaction = null)
        {
            using var db = sqlConnectionFactory.Create();

            var parameters = new DynamicParameters();
            parameters.Add(
                name: $"@{dt.TableName}",
                value: dt.AsTableValuedParameter(mapToDbType));

            return await db.ExecuteAsync(
                sqlQuery,
                parameters,
                transaction: dbTransaction,
                commandTimeout: sqlOptions.Value.SqlWriteCommandTimeoutSeconds);
        }

        public async Task<int> ExecuteAsync(
            string sqlQuery,
            IDbTransaction dbTransaction = null)
        {
            using var db = sqlConnectionFactory.Create();

            return await db.ExecuteAsync(
                sqlQuery,
                transaction: dbTransaction,
                commandTimeout: sqlOptions.Value.SqlWriteCommandTimeoutSeconds);
        }
    }
}
