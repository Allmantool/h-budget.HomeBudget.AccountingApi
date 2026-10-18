using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    public sealed partial class PortabilityExportService
    {
        private async Task WriteContractFilesAsync(
            PortabilityExportFileWriter writer,
            List<PortabilityExportArtifact> artifacts,
            CancellationToken cancellationToken)
        {
            artifacts.Add(await writer.WriteBytesAsync(
                "README.md",
                "text/markdown",
                _catalog.ReadReadmeBytes(),
                cancellationToken).ConfigureAwait(false));
            foreach (var entry in _catalog.Entries.Values
                .Where(value => value.Kind == "schema")
                .OrderBy(value => value.Path, StringComparer.Ordinal))
            {
                artifacts.Add(await writer.WriteBytesAsync(
                    entry.Path,
                    "application/json",
                    _catalog.ReadSchemaBytes(entry.Path),
                    cancellationToken).ConfigureAwait(false));
            }
        }

        private async Task WriteFullBusinessStateAsync(
            PortabilityExportRequest request,
            PortabilityExportCutoff cutoff,
            PortabilityExportFileWriter writer,
            List<PortabilityExportArtifact> artifacts,
            PortabilityExportSummary summary,
            CancellationToken cancellationToken)
        {
            await WriteReferencesAsync(
                request,
                cutoff,
                writer,
                artifacts,
                summary,
                includeAll: true,
                cancellationToken).ConfigureAwait(false);
            await WriteSettingsAsync(request, cutoff, writer, artifacts, summary, cancellationToken).ConfigureAwait(false);
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "rates.jsonl",
                "schemas/rate.schema.json",
                _reader.ReadRatesAsync(cutoff, request, cancellationToken),
                _ => true,
                Identity,
                summary.ObserveRate,
                cancellationToken).ConfigureAwait(false));
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "operations.jsonl",
                "schemas/operation.schema.json",
                _reader.ReadOperationsAsync(cutoff, request, cancellationToken),
                _ => true,
                Identity,
                summary.ObserveOperation,
                cancellationToken).ConfigureAwait(false));
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "lineage.jsonl",
                "schemas/lineage.schema.json",
                _reader.ReadLineageAsync(cutoff, request, cancellationToken),
                _ => true,
                Identity,
                summary.ObserveLineage,
                cancellationToken).ConfigureAwait(false));
        }

        private async Task WritePartialBusinessStateAsync(
            PortabilityExportRequest request,
            PortabilityExportCutoff cutoff,
            PortabilityExportFileWriter writer,
            List<PortabilityExportArtifact> artifacts,
            PortabilityExportSummary summary,
            CancellationToken cancellationToken)
        {
            if (request.SelectedOperationIds.Count > _options.MaxBufferedRecords)
            {
                throw new PortabilityExportValidationException("export.partial-selection-limit");
            }

            var selectedIds = request.SelectedOperationIds.ToHashSet(StringComparer.Ordinal);
            var operations = new List<JsonElement>(selectedIds.Count);
            await foreach (var operation in _reader.ReadOperationsAsync(cutoff, request, cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (selectedIds.Contains(operation.GetProperty("id").GetString()))
                {
                    var copy = operation.Clone();
                    operations.Add(copy);
                    summary.CaptureOperationDependencies(copy);
                }
            }

            if (operations.Count != selectedIds.Count)
            {
                throw new PortabilityExportValidationException("export.partial-operation-missing");
            }

            await WriteReferencesAsync(
                request,
                cutoff,
                writer,
                artifacts,
                summary,
                includeAll: false,
                cancellationToken).ConfigureAwait(false);
            await WriteSettingsAsync(request, cutoff, writer, artifacts, summary, cancellationToken).ConfigureAwait(false);
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "rates.jsonl",
                "schemas/rate.schema.json",
                _reader.ReadRatesAsync(cutoff, request, cancellationToken),
                value => summary.RateIds.Contains(value.GetProperty("id").GetString()),
                Identity,
                summary.ObserveRate,
                cancellationToken).ConfigureAwait(false));
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "operations.jsonl",
                "schemas/operation.schema.json",
                ToAsync(operations, cancellationToken),
                _ => true,
                Identity,
                summary.ObserveOperation,
                cancellationToken).ConfigureAwait(false));
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "lineage.jsonl",
                "schemas/lineage.schema.json",
                _reader.ReadLineageAsync(cutoff, request, cancellationToken),
                value => summary.LineageIds.Contains(value.GetProperty("id").GetString()),
                Identity,
                summary.ObserveLineage,
                cancellationToken).ConfigureAwait(false));
        }

        private async Task WriteReferencesAsync(
            PortabilityExportRequest request,
            PortabilityExportCutoff cutoff,
            PortabilityExportFileWriter writer,
            List<PortabilityExportArtifact> artifacts,
            PortabilityExportSummary summary,
            bool includeAll,
            CancellationToken cancellationToken)
        {
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "accounts.jsonl",
                "schemas/account.schema.json",
                _reader.ReadAccountsAsync(cutoff, request, cancellationToken),
                value => includeAll || summary.AccountIds.Contains(value.GetProperty("id").GetString()),
                Identity,
                summary.ObserveAccount,
                cancellationToken).ConfigureAwait(false));
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "currencies.jsonl",
                "schemas/currency.schema.json",
                _reader.ReadCurrenciesAsync(cutoff, request, cancellationToken),
                value => includeAll || summary.CurrencyIds.Contains(value.GetProperty("id").GetString()),
                Identity,
                summary.ObserveCurrency,
                cancellationToken).ConfigureAwait(false));
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "categories.jsonl",
                "schemas/category.schema.json",
                _reader.ReadCategoriesAsync(cutoff, request, cancellationToken),
                value => includeAll || summary.CategoryIds.Contains(value.GetProperty("id").GetString()),
                Identity,
                summary.ObserveCategory,
                cancellationToken).ConfigureAwait(false));
            artifacts.Add(await writer.WriteJsonLinesAsync(
                "contractors.jsonl",
                "schemas/contractor.schema.json",
                _reader.ReadContractorsAsync(cutoff, request, cancellationToken),
                value => includeAll || summary.ContractorIds.Contains(value.GetProperty("id").GetString()),
                Identity,
                summary.ObserveContractor,
                cancellationToken).ConfigureAwait(false));
        }

        private async Task WriteSettingsAsync(
            PortabilityExportRequest request,
            PortabilityExportCutoff cutoff,
            PortabilityExportFileWriter writer,
            List<PortabilityExportArtifact> artifacts,
            PortabilityExportSummary summary,
            CancellationToken cancellationToken)
        {
            var source = await ReadSingleAsync(
                _reader.ReadSettingsAsync(cutoff, request, cancellationToken),
                "export.settings-cardinality",
                cancellationToken).ConfigureAwait(false);
            var settings = PortabilityExportJson.FilterSettings(source);
            artifacts.Add(await writer.WriteJsonAsync(
                "settings.json",
                "schemas/settings.schema.json",
                settings,
                cancellationToken).ConfigureAwait(false));
            summary.Settings = 1;
        }

        private async Task WriteReconciliationAsync(
            PortabilityExportRequest request,
            PortabilityExportCutoff cutoff,
            PortabilityExportFileWriter writer,
            List<PortabilityExportArtifact> artifacts,
            PortabilityExportSummary summary,
            CancellationToken cancellationToken)
        {
            var source = await ReadSingleAsync(
                _reader.ReadReconciliationAsync(cutoff, request, cancellationToken),
                "export.reconciliation-cardinality",
                cancellationToken).ConfigureAwait(false);
            var reconciliation = PortabilityExportJson.Reconciliation(source, cutoff, summary);
            artifacts.Add(await writer.WriteJsonAsync(
                "reconciliation.json",
                "schemas/reconciliation.schema.json",
                reconciliation,
                cancellationToken).ConfigureAwait(false));
        }

        private static async Task<JsonElement> ReadSingleAsync(
            IAsyncEnumerable<JsonElement> records,
            string code,
            CancellationToken cancellationToken)
        {
            JsonElement result = default;
            var count = 0;
            await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                count++;
                if (count > 1)
                {
                    throw new PortabilityExportValidationException(code);
                }

                result = record.Clone();
            }

            return count == 1 ? result : throw new PortabilityExportValidationException(code);
        }

        private static JsonElement Identity(JsonElement value) => value;

        private static async IAsyncEnumerable<JsonElement> ToAsync(
            IEnumerable<JsonElement> records,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
                await Task.Yield();
            }
        }
    }
}
