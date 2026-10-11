using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public interface IActiveLedgerGenerationResolver
    {
        Task<LedgerGenerationScope> GetActiveAsync(CancellationToken cancellationToken = default);
    }
}
