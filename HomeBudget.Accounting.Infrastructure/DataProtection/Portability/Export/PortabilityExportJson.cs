using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    internal static class PortabilityExportJson
    {
        private static readonly string[] SettingProperties =
        [
            "defaultCurrencyId",
            "timeZone",
            "firstDayOfWeek",
            "fiscalYearStartMonth",
            "updatedUtc"
        ];

        public static JsonElement FilterSettings(JsonElement source)
        {
            var settings = new JsonObject();
            foreach (var name in SettingProperties)
            {
                if (source.TryGetProperty(name, out var value))
                {
                    settings[name] = JsonNode.Parse(value.GetRawText());
                }
            }

            return Parse(settings);
        }

        public static JsonElement Reconciliation(
            JsonElement source,
            PortabilityExportCutoff cutoff,
            PortabilityExportSummary summary)
        {
            var root = JsonNode.Parse(source.GetRawText()).AsObject();
            root["commonCutoffUtc"] = Utc(cutoff.ActualCutoffUtc);
            root["counts"] = new JsonObject
            {
                ["accounts"] = summary.Accounts,
                ["currencies"] = summary.Currencies,
                ["categories"] = summary.Categories,
                ["contractors"] = summary.Contractors,
                ["settings"] = summary.Settings,
                ["operations"] = summary.Operations,
                ["openingBalances"] = summary.OpeningBalances,
                ["incomeOperations"] = summary.IncomeOperations,
                ["expenseOperations"] = summary.ExpenseOperations,
                ["adjustmentOperations"] = summary.AdjustmentOperations,
                ["transferOperations"] = summary.TransferOperations,
                ["splitLines"] = summary.SplitLines,
                ["rates"] = summary.Rates,
                ["lineage"] = summary.Lineage
            };
            root["operationIdentitySha256"] = summary.OperationIdentitySha256;
            return Parse(root);
        }

        public static JsonElement Manifest(
            PortabilityExportRequest request,
            PortabilityExportCutoff cutoff,
            IEnumerable<PortabilityExportArtifact> artifacts)
        {
            var files = new JsonArray();
            foreach (var artifact in artifacts.OrderBy(value => value.Path, StringComparer.Ordinal))
            {
                var entry = new JsonObject
                {
                    ["path"] = artifact.Path,
                    ["mediaType"] = artifact.MediaType,
                    ["byteSize"] = artifact.ByteSize,
                    ["sha256"] = artifact.Sha256
                };
                if (artifact.Schema != null)
                {
                    entry["schema"] = artifact.Schema;
                }

                if (artifact.RecordCount.HasValue)
                {
                    entry["recordCount"] = artifact.RecordCount.Value;
                }

                files.Add(entry);
            }

            var manifest = new JsonObject
            {
                ["format"] = "home-ledger-portability",
                ["formatVersion"] = "1.0.0",
                ["profile"] = "business-state",
                ["completeness"] = request.Completeness,
                ["exportId"] = request.ExportId.ToString("D"),
                ["environmentId"] = request.EnvironmentId,
                ["ledgerId"] = request.LedgerId,
                ["sourceGeneration"] = new JsonObject
                {
                    ["id"] = cutoff.SourceGenerationId,
                    ["number"] = cutoff.SourceGenerationNumber
                },
                ["createdUtc"] = Utc(cutoff.ActualCutoffUtc),
                ["commonCutoffUtc"] = Utc(cutoff.ActualCutoffUtc),
                ["projectionWatermarkUtc"] = Utc(cutoff.ProjectionWatermarkUtc),
                ["attachments"] = new JsonObject { ["status"] = "unsupported-not-present" },
                ["reconciliationEntry"] = "reconciliation.json",
                ["files"] = files
            };
            return Parse(manifest);
        }

        public static string Utc(DateTimeOffset value) => value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        private static JsonElement Parse(JsonNode node)
        {
            using var document = JsonDocument.Parse(node.ToJsonString());
            return document.RootElement.Clone();
        }
    }
}
