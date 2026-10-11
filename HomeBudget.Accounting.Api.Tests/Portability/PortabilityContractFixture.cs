using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Json.Schema;

namespace HomeBudget.Accounting.Api.Tests.Portability
{
    internal sealed class PortabilityContractFixture
    {
        private const string ContractRelativePath = "contracts/home-ledger-portability/v1";
        private readonly BuildOptions _buildOptions = new()
        {
            SchemaRegistry = new SchemaRegistry()
        };
        private readonly Dictionary<string, JsonSchema> _schemas = new(StringComparer.Ordinal);

        public PortabilityContractFixture()
        {
            ContractRoot = Path.Combine(FindRepositoryRoot(), ContractRelativePath);
            SampleRoot = Path.Combine(ContractRoot, "samples", "full-ledger");
        }

        public string ContractRoot { get; }

        public string SampleRoot { get; }

        public JsonObject ReadContractObject(string relativePath) =>
            ReadObject(Path.Combine(ContractRoot, Normalize(relativePath)));

        public JsonObject ReadSampleObject(string relativePath) =>
            ReadObject(Path.Combine(SampleRoot, Normalize(relativePath)));

        public IReadOnlyCollection<JsonObject> ReadSampleLines(string relativePath)
        {
            var path = Path.Combine(SampleRoot, Normalize(relativePath));
            return File.ReadLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonNode.Parse(line)?.AsObject()
                    ?? throw new InvalidDataException($"A JSONL record in '{relativePath}' is null."))
                .ToArray();
        }

        public EvaluationResults Evaluate(string schemaPath, JsonNode instance)
        {
            if (!_schemas.TryGetValue(schemaPath, out var schema))
            {
                var fullPath = Path.Combine(SampleRoot, Normalize(schemaPath));
                schema = JsonSchema.FromText(File.ReadAllText(fullPath), _buildOptions);
                _schemas.Add(schemaPath, schema);
            }

            return schema.Evaluate(
                JsonSerializer.SerializeToElement(instance),
                new EvaluationOptions
                {
                    OutputFormat = OutputFormat.List,
                    RequireFormatValidation = true
                });
        }

        public IReadOnlyCollection<string> SampleEntries() => Directory
            .EnumerateFiles(SampleRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(SampleRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        public static string Sha256(string path) =>
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        public static string Sha256Utf8(string value) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        public string SamplePath(string relativePath) => Path.Combine(SampleRoot, Normalize(relativePath));

        public string ContractPath(string relativePath) => Path.Combine(ContractRoot, Normalize(relativePath));

        private static JsonObject ReadObject(string path) =>
            JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException($"JSON object '{path}' is null.");

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "HomeBudgetAccountingApi.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new DirectoryNotFoundException("The Accounting repository root could not be located.");
        }

        private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar);
    }
}
