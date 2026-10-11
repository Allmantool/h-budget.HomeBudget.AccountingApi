using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.Tests.Portability.Export
{
    internal sealed class PortabilityExportScenario
    {
        private const string AccountsReader = "ReadAccountsAsync";
        private const string CategoriesReader = "ReadCategoriesAsync";
        private const string ContractorsReader = "ReadContractorsAsync";
        private const string CurrenciesReader = "ReadCurrenciesAsync";
        private const string LineageReader = "ReadLineageAsync";
        private const string OperationsReader = "ReadOperationsAsync";
        private const string RatesReader = "ReadRatesAsync";
        private const string ReconciliationReader = "ReadReconciliationAsync";
        private const string SettingsReader = "ReadSettingsAsync";
        private readonly Dictionary<string, IReadOnlyList<JsonElement>> _records;
        private long _baselineManagedBytes;

        public PortabilityExportScenario(PortabilityContractFixture contract, string destinationPath)
        {
            DestinationPath = destinationPath;
            _records = new Dictionary<string, IReadOnlyList<JsonElement>>(StringComparer.Ordinal)
            {
                [AccountsReader] = ReadJsonLines(contract.SamplePath("accounts.jsonl")),
                [CategoriesReader] = ReadJsonLines(contract.SamplePath("categories.jsonl")),
                [ContractorsReader] = ReadJsonLines(contract.SamplePath("contractors.jsonl")),
                [CurrenciesReader] = ReadJsonLines(contract.SamplePath("currencies.jsonl")),
                [LineageReader] = ReadJsonLines(contract.SamplePath("lineage.jsonl")),
                [OperationsReader] = AddArchivedOperation(
                    ReadJsonLines(contract.SamplePath("operations.jsonl"))),
                [RatesReader] = ReadJsonLines(contract.SamplePath("rates.jsonl")),
                [ReconciliationReader] = [ReadJson(contract.SamplePath("reconciliation.json"))],
                [SettingsReader] = [AddSensitiveSettings(ReadJson(contract.SamplePath("settings.json")))]
            };
        }

        public string DestinationPath { get; }

        public bool ProjectionCanCatchUp { get; set; } = true;

        public bool ProjectionStartsBehind { get; set; } = true;

        public string Completeness { get; set; } = "full";

        public string[] SelectedOperationIds { get; set; } = [];

        public string ThrowOnReader { get; set; }

        public int GeneratedOperationCount { get; set; }

        public string FirstAccountName { get; set; }

        public List<string> Calls { get; } = [];

        public List<long> ObservedCutoffSequences { get; } = [];

        public bool DestinationObservedBeforeSourceCompletion { get; private set; }

        public long MaximumObservedManagedBytes { get; private set; }

        public string RequestJson => JsonSerializer.Serialize(
            new
            {
                exportId = "a0000000-0000-4000-8000-000000000001",
                environmentId = "disposable-unit-test",
                ledgerId = "default",
                ledgerGenerationId = "legacy",
                ledgerGenerationNumber = 0,
                completeness = Completeness,
                selectedOperationIds = SelectedOperationIds,
                requestedCutoffUtc = "2025-01-15T12:30:00Z",
                destinationPath = DestinationPath
            });

        public string CutoffJson => JsonSerializer.Serialize(
            new
            {
                requestedCutoffUtc = "2025-01-15T12:30:00Z",
                actualCutoffUtc = "2025-01-15T12:30:00Z",
                commonCutoffSequence = 4242,
                projectionWatermarkSequence = ProjectionStartsBehind ? 4241 : 4242,
                projectionWatermarkUtc = ProjectionStartsBehind
                    ? "2025-01-15T12:29:59Z"
                    : "2025-01-15T12:30:00Z",
                sourceGenerationId = "legacy",
                sourceGenerationNumber = 0
            });

        public void BeginMemoryObservation()
        {
            _baselineManagedBytes = GC.GetTotalMemory(true);
            MaximumObservedManagedBytes = 0;
        }

        public void RecordCall(string methodName, object[] arguments)
        {
            Calls.Add(methodName);
            foreach (var argument in arguments.Where(value => value != null))
            {
                var property = argument.GetType().GetProperty("CommonCutoffSequence")
                    ?? argument.GetType().GetProperty("CutoffSequence");
                if (property?.GetValue(argument) is long sequence)
                {
                    ObservedCutoffSequences.Add(sequence);
                }
            }
        }

        public async IAsyncEnumerable<JsonElement> ReadAsync(
            string methodName,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (string.Equals(ThrowOnReader, methodName, StringComparison.Ordinal))
            {
                throw new IOException("Synthetic export source failure without payload data.");
            }

            if (methodName == OperationsReader && GeneratedOperationCount > 0)
            {
                for (var index = 0; index < GeneratedOperationCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ObserveMemoryAndPublication();
                    yield return CreateGeneratedOperation(index);
                    await Task.Yield();
                }

                yield break;
            }

            if (!_records.TryGetValue(methodName, out var records))
            {
                throw new InvalidOperationException($"Unexpected export reader method '{methodName}'.");
            }

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObserveMemoryAndPublication();
                yield return OverrideAccountName(methodName, record);
                await Task.Yield();
            }
        }

        private static JsonElement[] ReadJsonLines(string path) => File
            .ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseJson)
            .ToArray();

        private static JsonElement ReadJson(string path) => ParseJson(File.ReadAllText(path));

        private static JsonElement ParseJson(string value)
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }

        private static JsonElement[] AddArchivedOperation(
            JsonElement[] operations)
        {
            var archived = JsonNode.Parse(operations[3].GetRawText()).AsObject();
            archived["id"] = "60000000-0000-4000-8000-000000000007";
            archived["archived"] = true;
            archived["archivedUtc"] = "2025-01-15T12:20:00Z";
            archived["note"] = "Synthetic deleted operation tombstone";
            return operations.Append(ParseJson(archived.ToJsonString())).ToArray();
        }

        private static JsonElement AddSensitiveSettings(JsonElement settings)
        {
            var source = JsonNode.Parse(settings.GetRawText()).AsObject();
            source["connectionString"] = "Server=never-export.example;Password=do-not-export";
            source["password"] = "do-not-export-password";
            source["sessionToken"] = "do-not-export-token";
            source["encryptionKey"] = "do-not-export-key";
            return ParseJson(source.ToJsonString());
        }

        private static JsonElement CreateGeneratedOperation(int index)
        {
            var identifier = index.ToString("x12", CultureInfo.InvariantCulture);
            return ParseJson(JsonSerializer.Serialize(new
            {
                id = $"60000000-0000-4000-8000-{identifier}",
                kind = "expense",
                accountId = "40000000-0000-4000-8000-000000000001",
                amount = "1.00",
                direction = "debit",
                currencyId = "10000000-0000-4000-8000-000000000001",
                businessDate = "2025-01-15",
                originalCreatedUtc = "2025-01-15T12:00:00Z",
                updatedUtc = "2025-01-15T12:00:00Z",
                categoryId = "20000000-0000-4000-8000-000000000003",
                contractorId = "30000000-0000-4000-8000-000000000002",
                note = (string)null,
                splits = Array.Empty<object>(),
                archived = false,
                archivedUtc = (string)null,
                lineageId = (string)null
            }));
        }

        private JsonElement OverrideAccountName(string methodName, JsonElement record)
        {
            if (methodName != AccountsReader
                || FirstAccountName == null
                || record.GetProperty("id").GetString() != "40000000-0000-4000-8000-000000000001")
            {
                return record;
            }

            var account = JsonNode.Parse(record.GetRawText()).AsObject();
            account["name"] = FirstAccountName;
            return ParseJson(account.ToJsonString());
        }

        private void ObserveMemoryAndPublication()
        {
            DestinationObservedBeforeSourceCompletion |= File.Exists(DestinationPath);
            if (_baselineManagedBytes != 0)
            {
                MaximumObservedManagedBytes = Math.Max(
                    MaximumObservedManagedBytes,
                    GC.GetTotalMemory(false) - _baselineManagedBytes);
            }
        }
    }
}
