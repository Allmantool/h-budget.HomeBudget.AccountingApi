using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public interface IRecoveryPlanningEvidenceSource
    {
        Task<RecoveryEvidenceInventory> LoadAsync(
            RecoveryPlanningRequest request,
            CancellationToken cancellationToken);
    }
}
