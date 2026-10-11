using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal interface IPendingWorkSource
    {
        Task<PendingWorkSnapshot> InspectAsync(
            MaintenanceEpoch epoch,
            CancellationToken cancellationToken);

        Task<PendingWorkSnapshot> DrainAsync(
            MaintenanceEpoch epoch,
            CancellationToken cancellationToken);
    }
}
