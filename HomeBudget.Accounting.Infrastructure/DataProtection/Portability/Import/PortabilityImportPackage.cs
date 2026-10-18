using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

using HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal sealed class PortabilityImportPackage
    {
        private PortabilityImportPackage(
            string rootPath,
            JsonElement manifest,
            IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> records,
            JsonElement settings,
            JsonElement reconciliation,
            string packageSha256,
            long inventoryBytes)
        {
            RootPath = rootPath;
            Manifest = manifest;
            Records = records;
            Settings = settings;
            Reconciliation = reconciliation;
            PackageSha256 = packageSha256;
            InventoryBytes = inventoryBytes;
        }

        public string RootPath { get; }

        public JsonElement Manifest { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<JsonElement>> Records { get; }

        public JsonElement Settings { get; }

        public JsonElement Reconciliation { get; }

        public string PackageSha256 { get; }

        public long InventoryBytes { get; }

        public IReadOnlyList<PortabilityImportRecord> CreateExecutionRecords(
            IReadOnlyList<PortabilityImportEntityBinding> bindings)
        {
            var payloads = new Dictionary<(string EntityType, string SourceEntityId), JsonElement>();
            foreach (var entry in Records)
            {
                var entityType = entry.Key[..entry.Key.IndexOf('.', StringComparison.Ordinal)];
                foreach (var payload in entry.Value)
                {
                    payloads.Add((entityType, payload.GetProperty("id").GetString()), payload);
                }
            }

            payloads.Add(("settings", "settings"), Settings);
            var records = new List<PortabilityImportRecord>(bindings.Count);
            foreach (var binding in bindings)
            {
                if (!payloads.TryGetValue((binding.EntityType, binding.SourceEntityId), out var payload))
                {
                    throw Reject("import.entity-payload-missing", binding.EntryPath);
                }

                records.Add(new PortabilityImportRecord(
                    binding.EntityType,
                    binding.SourceEntityId,
                    binding.Fingerprint,
                    binding.IdempotencyKey,
                    binding.EntryPath,
                    payload.Clone()));
            }

            return records;
        }

        public static PortabilityImportPackage Load(
            string packageRoot,
            PortabilityImportPreflightOptions options,
            CancellationToken cancellationToken)
        {
            var root = ValidateRoot(packageRoot);
            var catalog = PortabilityContractCatalog.Load();
            var validator = new PortabilityExportContractValidator(catalog);
            var manifest = ReadJson(Path.Combine(root, "manifest.json"), 64);
            if (!manifest.TryGetProperty("formatVersion", out var version)
                || version.ValueKind != JsonValueKind.String
                || !version.GetString().StartsWith("1.0.", StringComparison.Ordinal))
            {
                throw Reject("import.version-unsupported", "manifest.json");
            }

            Validate(validator, "schemas/manifest.schema.json", manifest, "manifest.json");
            var inventoryBytes = ValidateInventory(root, manifest, catalog, cancellationToken);
            var records = new Dictionary<string, IReadOnlyList<JsonElement>>(StringComparer.Ordinal);
            long recordCount = 0;
            foreach (var entry in catalog.Entries.Values.Where(value => value.Kind == "jsonl"))
            {
                var values = ReadLines(
                    Path.Combine(root, entry.Path),
                    ref recordCount,
                    options.MaxRecords,
                    cancellationToken);
                foreach (var value in values)
                {
                    Validate(validator, entry.Schema, value, entry.Path);
                }

                records.Add(entry.Path, values);
            }

            var settings = ReadJson(Path.Combine(root, "settings.json"), 64);
            Validate(validator, "schemas/settings.schema.json", settings, "settings.json");
            var reconciliation = ReadJson(Path.Combine(root, "reconciliation.json"), 64);
            Validate(
                validator,
                "schemas/reconciliation.schema.json",
                reconciliation,
                "reconciliation.json");
            return new PortabilityImportPackage(
                root,
                manifest,
                records,
                settings,
                reconciliation,
                ComputePackageSha256(root, cancellationToken),
                inventoryBytes);
        }

        private static string ValidateRoot(string packageRoot)
        {
            if (string.IsNullOrWhiteSpace(packageRoot))
            {
                throw Reject("import.package-root-invalid");
            }

            var root = Path.GetFullPath(packageRoot);
            if (!Directory.Exists(root)
                || Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar) == root.TrimEnd(Path.DirectorySeparatorChar)
                || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw Reject("import.package-root-invalid");
            }

            return root;
        }

        private static long ValidateInventory(
            string root,
            JsonElement manifest,
            PortabilityContractCatalog catalog,
            CancellationToken cancellationToken)
        {
            long total = 0;
            var expected = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" };
            foreach (var entry in manifest.GetProperty("files").EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = entry.GetProperty("path").GetString();
                if (!catalog.Entries.ContainsKey(path) || !expected.Add(path))
                {
                    throw Reject("import.inventory-invalid", path);
                }

                var fullPath = Resolve(root, path);
                if (!File.Exists(fullPath))
                {
                    throw Reject("import.entry-missing", path);
                }

                var length = new FileInfo(fullPath).Length;
                var hash = HashFile(fullPath, cancellationToken);
                if (length != entry.GetProperty("byteSize").GetInt64()
                    || hash != entry.GetProperty("sha256").GetString())
                {
                    throw Reject("import.checksum-mismatch", path);
                }

                total = checked(total + length);
            }

            var actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            if (!expected.SetEquals(actual))
            {
                throw Reject("import.inventory-invalid");
            }

            return total;
        }

        private static IReadOnlyList<JsonElement> ReadLines(
            string path,
            ref long total,
            long maximum,
            CancellationToken cancellationToken)
        {
            var records = new List<JsonElement>();
            foreach (var line in File.ReadLines(path).Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total++;
                if (total > maximum)
                {
                    throw Reject("import.record-count-limit", Path.GetFileName(path));
                }

                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                records.Add(document.RootElement.Clone());
            }

            return records;
        }

        private static JsonElement ReadJson(string path, int maxDepth)
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(path),
                new JsonDocumentOptions { MaxDepth = maxDepth });
            return document.RootElement.Clone();
        }

        private static string Resolve(string root, string relative)
        {
            var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, Comparison))
            {
                throw Reject("import.inventory-invalid", relative);
            }

            return path;
        }

        private static string ComputePackageSha256(string root, CancellationToken cancellationToken)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(value => Path.GetRelativePath(root, value), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                hash.AppendData(Encoding.UTF8.GetBytes(relative));
                hash.AppendData([0]);
                AppendFile(hash, path, cancellationToken);
                hash.AppendData([0]);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        private static string HashFile(string path, CancellationToken cancellationToken)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendFile(hash, path, cancellationToken);
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        private static void AppendFile(
            IncrementalHash hash,
            string path,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.SequentialScan);
            var buffer = new byte[81920];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    return;
                }

                hash.AppendData(buffer, 0, read);
            }
        }

        private static void Validate(
            PortabilityExportContractValidator validator,
            string schema,
            JsonElement value,
            string path)
        {
            try
            {
                validator.Validate(schema, value, path);
            }
            catch (PortabilityExportValidationException exception)
            {
                throw Reject("import.schema-validation", path, innerException: exception);
            }
        }

        private static StringComparison Comparison => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        private static PortabilityImportValidationException Reject(
            string code,
            string path = null,
            Exception innerException = null) => new(code, path, innerException: innerException);
    }
}
