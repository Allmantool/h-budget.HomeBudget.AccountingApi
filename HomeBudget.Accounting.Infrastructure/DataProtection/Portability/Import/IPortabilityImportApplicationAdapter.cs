using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public interface IPortabilityImportApplicationAdapter
    {
        Task<IReadOnlyList<PortabilityImportRecordResult>> AcceptBatchAsync(
            IReadOnlyList<PortabilityImportRecord> records,
            PortabilityImportExecutionContext context,
            CancellationToken cancellationToken);

        Task<PortabilityImportRecordResult> GetStatusAsync(
            PortabilityImportRecord record,
            PortabilityImportExecutionContext context,
            CancellationToken cancellationToken);
    }
}
