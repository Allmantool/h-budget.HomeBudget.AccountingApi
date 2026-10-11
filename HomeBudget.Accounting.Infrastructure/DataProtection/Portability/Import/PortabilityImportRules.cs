using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal static partial class PortabilityImportRules
    {
        public static IReadOnlyList<PortabilityImportEntityBinding> Validate(
            PortabilityImportPackage package,
            Guid exportId)
        {
            var state = new ValidationState(package);
            state.Index("currencies.jsonl", state.Currencies);
            state.Index("accounts.jsonl", state.Accounts);
            state.Index("categories.jsonl", state.Categories);
            state.Index("contractors.jsonl", state.Contractors);
            state.Index("rates.jsonl", state.Rates);
            state.Index("lineage.jsonl", state.Lineage);
            state.Index("operations.jsonl", state.Operations);
            ValidateReferences(state);
            ValidateOperations(state);
            ValidateLineage(state);
            return CreateBindings(package, exportId);
        }

        private static IReadOnlyList<PortabilityImportEntityBinding> CreateBindings(
            PortabilityImportPackage package,
            Guid exportId)
        {
            var bindings = new List<PortabilityImportEntityBinding>();
            foreach (var item in package.Records.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var entityType = item.Key[..item.Key.IndexOf('.', StringComparison.Ordinal)];
                foreach (var value in item.Value)
                {
                    var id = value.GetProperty("id").GetString();
                    bindings.Add(new PortabilityImportEntityBinding(
                        entityType,
                        id,
                        PortabilityImportFingerprint.Entity(value),
                        PortabilityImportFingerprint.Idempotency(exportId, item.Key, id),
                        item.Key));
                }
            }

            bindings.Add(new PortabilityImportEntityBinding(
                "settings",
                "settings",
                PortabilityImportFingerprint.Entity(package.Settings),
                PortabilityImportFingerprint.Idempotency(exportId, "settings.json", "settings"),
                "settings.json"));
            return bindings;
        }

        private static void ValidateReferences(ValidationState state)
        {
            foreach (var account in state.Accounts.Values)
            {
                Require(state.Currencies, account, "accounts.jsonl", "currencyId");
                Require(state.Operations, account, "accounts.jsonl", "openingBalanceOperationId");
            }

            foreach (var category in state.Categories.Values)
            {
                RequireOptional(state.Categories, category, "categories.jsonl", "parentId");
            }

            foreach (var rate in state.Rates.Values)
            {
                Require(state.Currencies, rate, "rates.jsonl", "baseCurrencyId");
                Require(state.Currencies, rate, "rates.jsonl", "quoteCurrencyId");
            }
        }

        private static void ValidateLineage(ValidationState state)
        {
            foreach (var lineage in state.Lineage.Values)
            {
                var target = lineage.GetProperty("entityType").GetString() switch
                {
                    "account" => state.Accounts,
                    "category" => state.Categories,
                    "contractor" => state.Contractors,
                    "operation" => state.Operations,
                    "rate" => state.Rates,
                    "setting" => null,
                    _ => null
                };
                if (target != null)
                {
                    Require(target, lineage, "lineage.jsonl", "entityId");
                }
            }
        }

        private static void Require(
            IReadOnlyDictionary<string, JsonElement> target,
            JsonElement source,
            string path,
            string field)
        {
            var id = source.GetProperty(field).GetString();
            if (!target.ContainsKey(id))
            {
                throw Reject("import.reference-missing", path, source.GetProperty("id").GetString(), field);
            }
        }

        private static void RequireOptional(
            IReadOnlyDictionary<string, JsonElement> target,
            JsonElement source,
            string path,
            string field)
        {
            if (source.GetProperty(field).ValueKind != JsonValueKind.Null)
            {
                Require(target, source, path, field);
            }
        }

        private static PortabilityImportValidationException Reject(
            string code,
            string path,
            string entity,
            string field) => new(code, path, entity, field);

        private sealed class ValidationState
        {
            public ValidationState(PortabilityImportPackage package)
            {
                Package = package;
            }

            public PortabilityImportPackage Package { get; }

            public Dictionary<string, JsonElement> Currencies { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, JsonElement> Accounts { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, JsonElement> Categories { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, JsonElement> Contractors { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, JsonElement> Rates { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, JsonElement> Lineage { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, JsonElement> Operations { get; } = new(StringComparer.Ordinal);

            public void Index(string path, Dictionary<string, JsonElement> target)
            {
                foreach (var value in Package.Records[path])
                {
                    var id = value.GetProperty("id").GetString();
                    if (!target.TryAdd(id, value))
                    {
                        throw Reject("import.identity-duplicate", path, id, "id");
                    }
                }
            }
        }
    }
}
