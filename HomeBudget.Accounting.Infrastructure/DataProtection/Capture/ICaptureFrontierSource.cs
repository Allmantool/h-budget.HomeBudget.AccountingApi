using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal interface ICaptureFrontierSource
    {
        string ComponentName { get; }

        Task<CaptureComponentReceipt> AcquireAsync(
            MaintenanceEpoch epoch,
            CancellationToken cancellationToken);
    }
}
