using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public interface IEventStoreDbProtectionProbe
    {
        Task<EventStoreDbSourceProbe> ProbeSourceAsync(CancellationToken cancellationToken);

        Task<EventStoreDbRestoreTargetProbe> ProbeRestoreTargetAsync(
            string targetId,
            CancellationToken cancellationToken);
    }
}
