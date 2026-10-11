using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability
{
    internal sealed class PortabilityContractCatalog
    {
        private const string ResourcePrefix = "HomeBudget.Accounting.Portability.v1.";
        private readonly Assembly _assembly;
        private readonly IReadOnlyDictionary<string, PortabilityCatalogEntry> _entries;

        private PortabilityContractCatalog(
            Assembly assembly,
            IReadOnlyDictionary<string, PortabilityCatalogEntry> entries)
        {
            _assembly = assembly;
            _entries = entries;
        }

        public IReadOnlyDictionary<string, PortabilityCatalogEntry> Entries => _entries;

        public IReadOnlyCollection<string> RequiredFullEntries => _entries.Values
            .Where(value => value.RequiredForFull)
            .Select(value => value.Path)
            .ToArray();

        public static PortabilityContractCatalog Load()
        {
            var assembly = typeof(PortabilityContractCatalog).Assembly;
            using var resource = OpenResource(assembly, $"{ResourcePrefix}entry-catalog.json");
            using var document = JsonDocument.Parse(resource);
            var entries = document.RootElement.GetProperty("entries")
                .EnumerateArray()
                .Select(value => new PortabilityCatalogEntry(
                    value.GetProperty("path").GetString(),
                    value.GetProperty("kind").GetString(),
                    value.GetProperty("requiredForFull").GetBoolean(),
                    value.TryGetProperty("schema", out var schema) ? schema.GetString() : null))
                .ToDictionary(value => value.Path, StringComparer.Ordinal);

            return new PortabilityContractCatalog(assembly, entries);
        }

        public byte[] ReadSchemaBytes(string path)
        {
            var name = Path.GetFileName(path);
            using var resource = OpenResource(_assembly, $"{ResourcePrefix}schemas.{name}");
            using var content = new MemoryStream();
            resource.CopyTo(content);
            return content.ToArray();
        }

        public byte[] ReadReadmeBytes()
        {
            using var resource = OpenResource(_assembly, $"{ResourcePrefix}README.md");
            using var content = new MemoryStream();
            resource.CopyTo(content);
            return content.ToArray();
        }

        private static Stream OpenResource(Assembly assembly, string name) =>
            assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Required embedded portability contract resource '{name}' is missing.");
    }

    internal sealed record PortabilityCatalogEntry(
        string Path,
        string Kind,
        bool RequiredForFull,
        string Schema);
}
