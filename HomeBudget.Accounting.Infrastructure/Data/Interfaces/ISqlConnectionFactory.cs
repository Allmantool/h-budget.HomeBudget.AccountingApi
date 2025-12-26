using System.Data;

namespace HomeBudget.Accounting.Infrastructure.Data.Interfaces
{
    public interface ISqlConnectionFactory
    {
        IDbConnection Create();
    }
}
