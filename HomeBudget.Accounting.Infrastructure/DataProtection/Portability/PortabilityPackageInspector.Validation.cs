using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Json.Schema;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability
{
    internal sealed partial class PortabilityPackageInspector
    {
        private async Task ValidateExtractedPackageAsync(
            ExtractedPackage package,
            string stagingPath,
            CancellationToken cancellationToken)
        {
            if (!package.Entries.TryGetValue("manifest.json", out var manifestEntry))
            {
                throw Reject("archive.required-entry-missing", "manifest.json");
            }

            await ValidatePackagedSchemasAsync(package, cancellationToken).ConfigureAwait(false);
            var schemas = LoadPackagedSchemas(package);
            using var manifest = await ParseJsonFileAsync(manifestEntry, cancellationToken).ConfigureAwait(false);
            ValidateSchema(schemas["schemas/manifest.schema.json"], manifest.RootElement, manifestEntry.Rule.Path);
            var inventory = ReadManifestInventory(manifest.RootElement);
            ValidateEntryClosure(package, manifest.RootElement, inventory);

            var recordCounts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var entry in package.Entries.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Rule.Kind == "json")
                {
                    if (entry.Rule.Path == "manifest.json")
                    {
                        continue;
                    }

                    using var document = await ParseJsonFileAsync(entry, cancellationToken).ConfigureAwait(false);
                    ValidateSchema(schemas[entry.Rule.Schema], document.RootElement, entry.Rule.Path);
                }
                else if (entry.Rule.Kind == "jsonl")
                {
                    recordCounts[entry.Rule.Path] = await ValidateJsonLinesAsync(
                        entry,
                        schemas[entry.Rule.Schema],
                        cancellationToken).ConfigureAwait(false);
                }
            }

            ValidateManifestInventory(package, inventory, recordCounts);
            if (Directory.EnumerateFileSystemEntries(stagingPath)
                .Any(path => !IsKnownStagingEntry(Path.GetFileName(path))))
            {
                throw Reject("archive.staging-integrity");
            }
        }

        private static bool IsKnownStagingEntry(string name) =>
            name is MarkerFileName or "content" or "archive.zip";

        private async Task ValidatePackagedSchemasAsync(
            ExtractedPackage package,
            CancellationToken cancellationToken)
        {
            foreach (var rule in _catalog.Entries.Values.Where(value => value.Kind == "schema"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!package.Entries.TryGetValue(rule.Path, out var entry))
                {
                    throw Reject("archive.required-entry-missing", rule.Path);
                }

                var packaged = await File.ReadAllBytesAsync(entry.FilePath, cancellationToken).ConfigureAwait(false);
                var normative = _catalog.ReadSchemaBytes(rule.Path);
                if (!packaged.AsSpan().SequenceEqual(normative))
                {
                    throw Reject("archive.schema-contract-mismatch", rule.Path);
                }
            }
        }

        private static Dictionary<string, JsonSchema> LoadPackagedSchemas(ExtractedPackage package)
        {
            var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
            var buildOptions = new BuildOptions
            {
                SchemaRegistry = new SchemaRegistry()
            };
            foreach (var entry in package.Entries.Values.Where(value => value.Rule.Kind == "schema"))
            {
                try
                {
                    schemas.Add(
                        entry.Rule.Path,
                        JsonSchema.FromText(File.ReadAllText(entry.FilePath), buildOptions));
                }
                catch (Exception exception) when (exception is JsonException or JsonSchemaException)
                {
                    throw PortabilityPackageValidationException.Create(
                        "archive.schema-invalid",
                        entry.Rule.Path,
                        fieldPath: "$",
                        innerException: exception);
                }
            }

            return schemas;
        }

        private async Task<JsonDocument> ParseJsonFileAsync(
            ExtractedEntry entry,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var input = new FileStream(
                    entry.FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    _options.BufferSizeBytes,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var document = await JsonDocument.ParseAsync(
                    input,
                    new JsonDocumentOptions { MaxDepth = _options.MaxJsonDepth },
                    cancellationToken).ConfigureAwait(false);
                RejectRuntimeTypeMetadata(document.RootElement, entry.Rule.Path, null);
                return document;
            }
            catch (JsonException exception)
            {
                var code = await IsDepthLimitAsync(entry.FilePath, cancellationToken).ConfigureAwait(false)
                    ? "archive.json-depth-limit"
                    : "archive.json-invalid";
                throw PortabilityPackageValidationException.Create(
                    code,
                    entry.Rule.Path,
                    fieldPath: "$",
                    innerException: exception);
            }
        }

        private async Task<long> ValidateJsonLinesAsync(
            ExtractedEntry entry,
            JsonSchema schema,
            CancellationToken cancellationToken)
        {
            await using var input = new FileStream(
                entry.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _options.BufferSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var readBuffer = new byte[_options.BufferSizeBytes];
            using var line = new MemoryStream(Math.Min(_options.MaxJsonLineBytes, _options.BufferSizeBytes));
            long recordNumber = 0;

            while (true)
            {
                var read = await input.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                recordNumber = ProcessJsonLineBytes(
                    readBuffer.AsSpan(0, read),
                    entry,
                    schema,
                    line,
                    recordNumber);
            }

            if (line.Length > 0)
            {
                recordNumber = ValidateJsonLine(entry, schema, line, recordNumber);
            }

            return recordNumber;
        }

        private long ProcessJsonLineBytes(
            ReadOnlySpan<byte> bytes,
            ExtractedEntry entry,
            JsonSchema schema,
            MemoryStream line,
            long recordNumber)
        {
            foreach (var value in bytes)
            {
                if (value == (byte)'\n')
                {
                    recordNumber = ValidateJsonLine(entry, schema, line, recordNumber);
                    line.SetLength(0);
                    continue;
                }

                if (line.Length >= _options.MaxJsonLineBytes)
                {
                    throw Reject("archive.line-length-limit", entry.Rule.Path);
                }

                line.WriteByte(value);
            }

            return recordNumber;
        }

        private long ValidateJsonLine(
            ExtractedEntry entry,
            JsonSchema schema,
            MemoryStream line,
            long currentRecord)
        {
            var length = checked((int)line.Length);
            if (length > 0 && line.GetBuffer()[length - 1] == (byte)'\r')
            {
                length--;
            }

            if (length == 0)
            {
                return currentRecord;
            }

            var recordNumber = checked(currentRecord + 1);
            if (recordNumber > _options.MaxJsonlRecordCount)
            {
                throw Reject("archive.record-count-limit", entry.Rule.Path);
            }

            try
            {
                using var document = JsonDocument.Parse(
                    line.GetBuffer().AsMemory(0, length),
                    new JsonDocumentOptions { MaxDepth = _options.MaxJsonDepth });
                RejectRuntimeTypeMetadata(document.RootElement, entry.Rule.Path, recordNumber);
                ValidateSchema(schema, document.RootElement, entry.Rule.Path, recordNumber);
            }
            catch (JsonException exception)
            {
                var code = IsValidAtHigherDepth(line.GetBuffer().AsMemory(0, length))
                    ? "archive.json-depth-limit"
                    : "archive.json-invalid";
                throw PortabilityPackageValidationException.Create(
                    code,
                    entry.Rule.Path,
                    recordNumber,
                    "$",
                    exception);
            }

            return recordNumber;
        }

        private static void ValidateSchema(
            JsonSchema schema,
            JsonElement value,
            string entryPath,
            long? recordNumber = null)
        {
            var results = schema.Evaluate(
                value,
                new EvaluationOptions
                {
                    OutputFormat = OutputFormat.List,
                    RequireFormatValidation = true
                });
            if (results.IsValid)
            {
                return;
            }

            var detail = results.Details.FirstOrDefault(result => !result.IsValid) ?? results;
            var fieldPath = detail.InstanceLocation.ToString();
            throw PortabilityPackageValidationException.Create(
                "archive.schema-validation",
                entryPath,
                recordNumber,
                string.IsNullOrWhiteSpace(fieldPath) ? "$" : fieldPath);
        }

        private static void RejectRuntimeTypeMetadata(
            JsonElement value,
            string entryPath,
            long? recordNumber)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                {
                    if (property.NameEquals("$type"))
                    {
                        throw PortabilityPackageValidationException.Create(
                            "archive.runtime-type-metadata",
                            entryPath,
                            recordNumber,
                            "$type");
                    }

                    RejectRuntimeTypeMetadata(property.Value, entryPath, recordNumber);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    RejectRuntimeTypeMetadata(item, entryPath, recordNumber);
                }
            }
        }

        private async Task<bool> IsDepthLimitAsync(string path, CancellationToken cancellationToken)
        {
            try
            {
                await using var input = File.OpenRead(path);
                using var ignored = await JsonDocument.ParseAsync(
                    input,
                    new JsonDocumentOptions { MaxDepth = 1024 },
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsValidAtHigherDepth(ReadOnlyMemory<byte> json)
        {
            try
            {
                using var ignored = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1024 });
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private void ValidateEntryClosure(
            ExtractedPackage package,
            JsonElement manifest,
            IReadOnlyDictionary<string, ManifestInventoryEntry> inventory)
        {
            var completeness = manifest.GetProperty("completeness").GetString();
            if (completeness == "full")
            {
                foreach (var required in _catalog.RequiredFullEntries)
                {
                    if (!package.Entries.ContainsKey(required))
                    {
                        throw Reject("archive.required-entry-missing", required);
                    }
                }
            }

            var actual = package.Entries.Keys
                .Where(path => path != "manifest.json")
                .ToHashSet(StringComparer.Ordinal);
            if (!actual.SetEquals(inventory.Keys))
            {
                throw Reject("archive.manifest-inventory-mismatch");
            }
        }

        private static Dictionary<string, ManifestInventoryEntry> ReadManifestInventory(
            JsonElement manifest)
        {
            var exact = new HashSet<string>(StringComparer.Ordinal);
            var caseInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inventory = new Dictionary<string, ManifestInventoryEntry>(StringComparer.Ordinal);
            foreach (var value in manifest.GetProperty("files").EnumerateArray())
            {
                var path = value.GetProperty("path").GetString();
                if (!exact.Add(path))
                {
                    throw Reject("archive.manifest-duplicate-entry");
                }

                if (!caseInsensitive.Add(path))
                {
                    throw Reject("archive.manifest-case-collision");
                }

                inventory.Add(
                    path,
                    new ManifestInventoryEntry(
                        value.GetProperty("mediaType").GetString(),
                        value.TryGetProperty("schema", out var schema) && schema.ValueKind != JsonValueKind.Null
                            ? schema.GetString()
                            : null,
                        value.TryGetProperty("recordCount", out var count) ? count.GetInt64() : null,
                        value.GetProperty("byteSize").GetInt64(),
                        value.GetProperty("sha256").GetString()));
            }

            return inventory;
        }

        private static void ValidateManifestInventory(
            ExtractedPackage package,
            IReadOnlyDictionary<string, ManifestInventoryEntry> inventory,
            Dictionary<string, long> recordCounts)
        {
            foreach (var item in inventory)
            {
                var extracted = package.Entries[item.Key];
                var expectedMediaType = MediaTypeFor(extracted.Rule.Kind);
                if (item.Value.MediaType != expectedMediaType
                    || item.Value.Schema != extracted.Rule.Schema
                    || item.Value.Bytes != extracted.Bytes
                    || !string.Equals(item.Value.Sha256, extracted.Sha256, StringComparison.Ordinal))
                {
                    var code = !string.Equals(item.Value.Sha256, extracted.Sha256, StringComparison.Ordinal)
                        ? "archive.checksum-mismatch"
                        : "archive.manifest-entry-mismatch";
                    throw Reject(code, extracted.Rule.Path);
                }

                if (extracted.Rule.Kind == "jsonl"
                    && (!item.Value.RecordCount.HasValue
                        || item.Value.RecordCount.Value != recordCounts[extracted.Rule.Path]))
                {
                    throw Reject("archive.record-count-mismatch", extracted.Rule.Path);
                }
            }
        }

        private static string MediaTypeFor(string kind) => kind switch
        {
            "json" or "schema" => "application/json",
            "jsonl" => "application/x-ndjson",
            "markdown" => "text/markdown",
            "csv" => "text/csv",
            _ => throw new InvalidOperationException($"Unsupported catalog kind '{kind}'.")
        };

        private sealed record ManifestInventoryEntry(
            string MediaType,
            string Schema,
            long? RecordCount,
            long Bytes,
            string Sha256);
    }
}
