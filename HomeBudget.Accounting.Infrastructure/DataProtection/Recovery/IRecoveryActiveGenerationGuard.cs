using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public interface IRecoveryActiveGenerationGuard
    {
        Task<string> GetActiveGenerationAsync(
            string environmentId,
            string ledgerId,
            CancellationToken cancellationToken);
    }
}
