using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public interface IDataProtectionOperationExecutionControl
    {
        Task<bool> IsCancellationRequestedAsync(
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken);

        Task SetStageAsync(
            DataProtectionOperationFenceContext fenceContext,
            string stage,
            CancellationToken cancellationToken);
    }
}
