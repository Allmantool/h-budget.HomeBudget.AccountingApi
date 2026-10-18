using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    public interface IPortabilityExportReader
    {
        Task<PortabilityExportCutoff> AcquireCutoffAsync(
            PortabilityExportRequest request,
            CancellationToken cancellationToken);

        Task<bool> WaitForProjectionAsync(
            PortabilityExportCutoff cutoff,
            CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadAccountsAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadCurrenciesAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadCategoriesAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadContractorsAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadSettingsAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadRatesAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadOperationsAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadLineageAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);

        IAsyncEnumerable<JsonElement> ReadReconciliationAsync(PortabilityExportCutoff cutoff, PortabilityExportRequest request, CancellationToken cancellationToken);
    }
}
