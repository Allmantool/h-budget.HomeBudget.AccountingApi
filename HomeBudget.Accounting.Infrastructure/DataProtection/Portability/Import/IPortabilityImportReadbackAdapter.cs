using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public interface IPortabilityImportReadbackAdapter
    {
        Task<PortabilityImportReconciliationSnapshot> ReadAsync(
            PortabilityImportExecutionContext context,
            int maximumRecords,
            CancellationToken cancellationToken);
    }
}
