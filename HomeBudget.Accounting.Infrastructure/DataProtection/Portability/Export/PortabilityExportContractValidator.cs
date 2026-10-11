using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Json.Schema;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    internal sealed class PortabilityExportContractValidator
    {
        private readonly Dictionary<string, JsonSchema> _schemas;

        public PortabilityExportContractValidator(PortabilityContractCatalog catalog)
        {
            var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
            _schemas = catalog.Entries.Values
                .Where(entry => entry.Kind == "schema")
                .ToDictionary(
                    entry => entry.Path,
                    entry => JsonSchema.FromText(
                        System.Text.Encoding.UTF8.GetString(catalog.ReadSchemaBytes(entry.Path)),
                        options),
                    StringComparer.Ordinal);
        }

        public void Validate(string schemaPath, JsonElement value, string entryPath)
        {
            var result = _schemas[schemaPath].Evaluate(
                value,
                new EvaluationOptions
                {
                    OutputFormat = OutputFormat.List,
                    RequireFormatValidation = true
                });
            if (result.IsValid)
            {
                return;
            }

            var detail = result.Details.FirstOrDefault(item => !item.IsValid) ?? result;
            var field = detail.InstanceLocation.ToString();
            throw new PortabilityExportValidationException(
                string.IsNullOrWhiteSpace(field)
                    ? $"export.schema-validation:{entryPath}:$"
                    : $"export.schema-validation:{entryPath}:{field}");
        }
    }
}
