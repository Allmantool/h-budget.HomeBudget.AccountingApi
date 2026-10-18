using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    internal sealed class PortabilityExportCsv
    {
        private readonly PortabilityExportFileWriter _writer;
        private readonly PortabilityExportStaging _staging;

        public PortabilityExportCsv(PortabilityExportFileWriter writer, PortabilityExportStaging staging)
        {
            _writer = writer;
            _staging = staging;
        }

        public async Task<IReadOnlyList<PortabilityExportArtifact>> WriteAsync(
            CancellationToken cancellationToken)
        {
            var artifacts = new List<PortabilityExportArtifact>
            {
                await WriteAccountsAsync(cancellationToken).ConfigureAwait(false),
                await WriteOperationsAsync(cancellationToken).ConfigureAwait(false),
                await WriteRatesAsync(cancellationToken).ConfigureAwait(false),
                await WriteSplitsAsync(cancellationToken).ConfigureAwait(false),
                await WriteTransfersAsync(cancellationToken).ConfigureAwait(false)
            };
            return artifacts;
        }

        private Task<PortabilityExportArtifact> WriteAccountsAsync(CancellationToken token) => _writer.WriteCsvAsync(
            "analysis/accounts.csv",
            ["id", "name", "accountType", "currencyId", "openingBalanceOperationId", "openedOn", "archived", "legacyReference"],
            Project("accounts.jsonl", value =>
            [
                Text(value, "id"), Text(value, "name"), Text(value, "accountType"), Text(value, "currencyId"),
                Text(value, "openingBalanceOperationId"), Text(value, "openedOn"), Text(value, "archived"), Text(value, "legacyReference")
            ]),
            token);

        private Task<PortabilityExportArtifact> WriteOperationsAsync(CancellationToken token) => _writer.WriteCsvAsync(
            "analysis/operations.csv",
            ["id", "kind", "accountId", "amount", "direction", "currencyId", "businessDate", "categoryId", "contractorId", "archived", "lineageId"],
            Project("operations.jsonl", value =>
            [
                Text(value, "id"), Text(value, "kind"), Text(value, "accountId"), Text(value, "amount"),
                Text(value, "direction"), Text(value, "currencyId"), Text(value, "businessDate"), Text(value, "categoryId"),
                Text(value, "contractorId"), Text(value, "archived"), Text(value, "lineageId")
            ]),
            token);

        private Task<PortabilityExportArtifact> WriteRatesAsync(CancellationToken token) => _writer.WriteCsvAsync(
            "analysis/rates.csv",
            ["id", "baseCurrencyId", "quoteCurrencyId", "rate", "validOn", "source", "capturedUtc"],
            Project("rates.jsonl", value =>
            [
                Text(value, "id"), Text(value, "baseCurrencyId"), Text(value, "quoteCurrencyId"),
                Text(value, "rate"), Text(value, "validOn"), Text(value, "source"), Text(value, "capturedUtc")
            ]),
            token);

        private Task<PortabilityExportArtifact> WriteSplitsAsync(CancellationToken token) => _writer.WriteCsvAsync(
            "analysis/splits.csv",
            ["operationId", "splitId", "categoryId", "amount", "memo"],
            ProjectMany("operations.jsonl", SplitRows),
            token);

        private Task<PortabilityExportArtifact> WriteTransfersAsync(CancellationToken token) => _writer.WriteCsvAsync(
            "analysis/transfers.csv",
            ["operationId", "sourceAccountId", "sourceAmount", "sourceCurrencyId", "destinationAccountId", "destinationAmount", "destinationCurrencyId", "rate", "rateId", "feeAmount", "feeCategoryId"],
            ProjectMany("operations.jsonl", TransferRows),
            token);

        private IEnumerable<IReadOnlyList<string>> Project(
            string path,
            Func<JsonElement, IReadOnlyList<string>> projector)
        {
            foreach (var line in File.ReadLines(_staging.GetContentPath(path)).Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                using var document = JsonDocument.Parse(line);
                yield return projector(document.RootElement);
            }
        }

        private IEnumerable<IReadOnlyList<string>> ProjectMany(
            string path,
            Func<JsonElement, IEnumerable<IReadOnlyList<string>>> projector)
        {
            foreach (var line in File.ReadLines(_staging.GetContentPath(path)).Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                using var document = JsonDocument.Parse(line);
                foreach (var row in projector(document.RootElement))
                {
                    yield return row;
                }
            }
        }

        private static IEnumerable<IReadOnlyList<string>> SplitRows(JsonElement operation)
        {
            if (!operation.TryGetProperty("splits", out var splits))
            {
                yield break;
            }

            foreach (var split in splits.EnumerateArray())
            {
                yield return
                [
                    Text(operation, "id"), Text(split, "id"), Text(split, "categoryId"),
                    Text(split, "amount"), Text(split, "memo")
                ];
            }
        }

        private static IEnumerable<IReadOnlyList<string>> TransferRows(JsonElement operation)
        {
            if (Text(operation, "kind") != "transfer")
            {
                yield break;
            }

            var source = operation.GetProperty("source");
            var destination = operation.GetProperty("destination");
            var rate = operation.GetProperty("exchangeRate");
            var fee = operation.GetProperty("fee");
            yield return
            [
                Text(operation, "id"), Text(source, "accountId"), Text(source, "amount"), Text(source, "currencyId"),
                Text(destination, "accountId"), Text(destination, "amount"), Text(destination, "currencyId"),
                Text(rate, "value"), Text(rate, "rateId"), Text(fee, "amount"), Text(fee, "categoryId")
            ];
        }

        private static string Text(JsonElement value, string name)
        {
            if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
            {
                return string.Empty;
            }

            return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
        }
    }
}
