using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public interface IPortabilityImportTargetProbe
    {
        Task<PortabilityImportTargetSnapshot> ProbeAsync(
            PortabilityImportRequest request,
            CancellationToken cancellationToken);
    }
}
