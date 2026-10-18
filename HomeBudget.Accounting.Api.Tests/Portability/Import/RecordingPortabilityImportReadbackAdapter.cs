using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import;

namespace HomeBudget.Accounting.Api.Tests.Portability.Import
{
    internal sealed class RecordingPortabilityImportReadbackAdapter : IPortabilityImportReadbackAdapter
    {
        public PortabilityImportReconciliationSnapshot Snapshot { get; set; }

        public int Calls { get; private set; }

        public Task<PortabilityImportReconciliationSnapshot> ReadAsync(
            PortabilityImportExecutionContext context,
            int maximumRecords,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Snapshot);
        }
    }
}
