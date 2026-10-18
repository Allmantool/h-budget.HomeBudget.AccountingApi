using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    public interface IMaintenanceAdmissionFence
    {
        Task<IMaintenanceAdmissionPermit> AcquireAdmissionPermitAsync(
            string scopeKey,
            string generationId,
            CancellationToken cancellationToken);

        Task<bool> IsAdmissionClosedAsync(
            string scopeKey,
            string generationId,
            CancellationToken cancellationToken);
    }
}
