using System.Collections.Generic;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.Data.Interfaces
{
    public interface IBaseReadRepository
    {
        Task<IReadOnlyCollection<T>> GetAsync<T>(string sqlQuery, object parameters = null);

        Task<T> SingleAsync<T>(string sqlQuery, object parameters);
    }
}
