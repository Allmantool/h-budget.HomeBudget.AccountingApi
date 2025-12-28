using System.Data;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.Data.Interfaces
{
    public interface IBaseWriteRepository
    {
        Task<int> ExecuteAsync<T>(string sqlQuery, T parameters, IDbTransaction dbTransaction = null)
            where T : IDbEntity;

        Task<int> ExecuteAsync<T>(string sqlQuery, IDbTransaction dbTransaction = null)
            where T : IDbEntity;

        Task<int> ExecuteAsync<T>(string sqlQuery, T[] parameters, IDbTransaction dbTransaction = null)
            where T : IDbEntity;

        Task<int> ExecuteAsync(string sqlQuery, IDbTransaction dbTransaction = null);

        Task<int> ExecuteAsync(string sqlQuery, DataTable dt, string mapToDbType, IDbTransaction dbTransaction = null);
    }
}
