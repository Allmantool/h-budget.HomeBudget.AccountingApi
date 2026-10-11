using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal static class PortabilityImportReconciliationComparer
    {
        public static PortabilityImportReconciliationComparison Compare(
            IReadOnlyList<PortabilityImportRecord> expected,
            JsonElement expectedSummary,
            PortabilityImportReconciliationSnapshot actual,
            int maximumRecords)
        {
            if (actual?.Records == null
                || actual.Records.Count > maximumRecords
                || actual.Summary.ValueKind != JsonValueKind.Object)
            {
                return Failed("import.reconciliation-readback-invalid", expected, expectedSummary, actual);
            }

            var duplicate = actual.Records.GroupBy(Key).FirstOrDefault(group => group.Skip(1).Any());
            if (duplicate != null)
            {
                return Failed(
                    "import.reconciliation-duplicate",
                    expected,
                    expectedSummary,
                    actual,
                    duplicate.Key.EntityType,
                    duplicate.Key.SourceEntityId);
            }

            var expectedById = expected.ToDictionary(Key);
            var actualById = actual.Records.ToDictionary(Key);
            var orphan = actualById.Keys.FirstOrDefault(key => !expectedById.ContainsKey(key));
            if (orphan != default)
            {
                return Failed(
                    "import.reconciliation-orphan",
                    expected,
                    expectedSummary,
                    actual,
                    orphan.EntityType,
                    orphan.SourceEntityId);
            }

            var missing = expectedById.Keys.FirstOrDefault(key => !actualById.ContainsKey(key));
            if (missing != default)
            {
                return Failed(
                    "import.reconciliation-missing-entity",
                    expected,
                    expectedSummary,
                    actual,
                    missing.EntityType,
                    missing.SourceEntityId);
            }

            var dangling = FindDanglingReference(actual.Records);
            if (dangling != null)
            {
                return Failed(
                    "import.reconciliation-orphan-reference",
                    expected,
                    expectedSummary,
                    actual,
                    dangling.Value.EntityType,
                    dangling.Value.SourceEntityId);
            }

            foreach (var pair in expectedById)
            {
                if (PortabilityImportFingerprint.Entity(actualById[pair.Key].Payload) != pair.Value.Fingerprint)
                {
                    return Failed(
                        "import.reconciliation-entity-mismatch",
                        expected,
                        expectedSummary,
                        actual,
                        pair.Key.EntityType,
                        pair.Key.SourceEntityId);
                }
            }

            var summaryMismatch = CompareSummary(expectedSummary, actual.Summary);
            return summaryMismatch == null
                ? Passed(expected, expectedSummary, actual)
                : Failed(summaryMismatch, expected, expectedSummary, actual);
        }

        private static string CompareSummary(JsonElement expected, JsonElement actual)
        {
            if (expected.GetProperty("commonCutoffUtc").GetString()
                != actual.GetProperty("commonCutoffUtc").GetString())
            {
                return "import.reconciliation-cutoff-mismatch";
            }

            if (Fingerprint(expected, "counts") != Fingerprint(actual, "counts"))
            {
                return "import.reconciliation-counts-mismatch";
            }

            if (Fingerprint(expected, "accountBalances") != Fingerprint(actual, "accountBalances"))
            {
                return "import.reconciliation-balances-mismatch";
            }

            if (Fingerprint(expected, "periodTotals") != Fingerprint(actual, "periodTotals"))
            {
                return "import.reconciliation-totals-mismatch";
            }

            return expected.GetProperty("operationIdentitySha256").GetString()
                == actual.GetProperty("operationIdentitySha256").GetString()
                ? null
                : "import.reconciliation-identities-mismatch";
        }

        private static (string EntityType, string SourceEntityId)? FindDanglingReference(
            IReadOnlyList<PortabilityImportRecord> records)
        {
            var ids = records.GroupBy(value => value.EntityType).ToDictionary(
                value => value.Key,
                value => value.Select(item => item.SourceEntityId).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (HasDanglingReference(record, ids))
                {
                    return (record.EntityType, record.SourceEntityId);
                }
            }

            return null;
        }

        private static bool HasDanglingReference(
            PortabilityImportRecord record,
            IReadOnlyDictionary<string, HashSet<string>> ids)
        {
            var value = record.Payload;
            return record.EntityType switch
            {
                "accounts" => Missing(value, "currencyId", ids, "currencies")
                    || Missing(value, "openingBalanceOperationId", ids, "operations"),
                "categories" => MissingOptional(value, "parentId", ids, "categories"),
                "rates" => Missing(value, "baseCurrencyId", ids, "currencies")
                    || Missing(value, "quoteCurrencyId", ids, "currencies"),
                "operations" => OperationHasDanglingReference(value, ids),
                _ => false
            };
        }

        private static bool OperationHasDanglingReference(
            JsonElement value,
            IReadOnlyDictionary<string, HashSet<string>> ids)
        {
            if (value.GetProperty("kind").GetString() == "transfer")
            {
                return TransferPartMissing(value.GetProperty("source"), ids)
                    || TransferPartMissing(value.GetProperty("destination"), ids)
                    || (value.TryGetProperty("fee", out var fee) && fee.ValueKind == JsonValueKind.Object
                        && (TransferPartMissing(fee, ids) || MissingOptional(fee, "categoryId", ids, "categories")))
                    || Missing(value.GetProperty("exchangeRate"), "rateId", ids, "rates");
            }

            if (Missing(value, "accountId", ids, "accounts")
                || Missing(value, "currencyId", ids, "currencies")
                || MissingOptional(value, "categoryId", ids, "categories")
                || MissingOptional(value, "contractorId", ids, "contractors"))
            {
                return true;
            }

            return value.GetProperty("splits").EnumerateArray()
                .Any(split => Missing(split, "categoryId", ids, "categories"));
        }

        private static bool TransferPartMissing(
            JsonElement value,
            IReadOnlyDictionary<string, HashSet<string>> ids) =>
            Missing(value, "accountId", ids, "accounts")
            || Missing(value, "currencyId", ids, "currencies");

        private static bool MissingOptional(
            JsonElement value,
            string property,
            IReadOnlyDictionary<string, HashSet<string>> ids,
            string entityType) => value.TryGetProperty(property, out var reference)
            && reference.ValueKind != JsonValueKind.Null
            && !ids[entityType].Contains(reference.GetString());

        private static bool Missing(
            JsonElement value,
            string property,
            IReadOnlyDictionary<string, HashSet<string>> ids,
            string entityType) => !ids[entityType].Contains(value.GetProperty(property).GetString());

        private static (string EntityType, string SourceEntityId) Key(PortabilityImportRecord record) =>
            (record.EntityType, record.SourceEntityId);

        private static string Fingerprint(JsonElement value, string property) =>
            PortabilityImportFingerprint.Entity(value.GetProperty(property));

        private static PortabilityImportReconciliationComparison Passed(
            IReadOnlyList<PortabilityImportRecord> expected,
            JsonElement expectedSummary,
            PortabilityImportReconciliationSnapshot actual) => new(
                true,
                Evidence(expected, expectedSummary, actual));

        private static PortabilityImportReconciliationComparison Failed(
            string errorCode,
            IReadOnlyList<PortabilityImportRecord> expected,
            JsonElement expectedSummary,
            PortabilityImportReconciliationSnapshot actual,
            string entityType = null,
            string entityId = null) => new(
                false,
                Evidence(expected, expectedSummary, actual),
                errorCode,
                entityType,
                entityId);

        private static string Evidence(
            IReadOnlyList<PortabilityImportRecord> expected,
            JsonElement expectedSummary,
            PortabilityImportReconciliationSnapshot actual)
        {
            var builder = new StringBuilder();
            foreach (var record in expected.OrderBy(Key))
            {
                builder.Append(record.EntityType).Append('\0')
                    .Append(record.SourceEntityId).Append('\0')
                    .Append(record.Fingerprint).Append('\n');
            }

            builder.Append(PortabilityImportFingerprint.Entity(expectedSummary)).Append('\n');
            if (actual?.Records != null)
            {
                foreach (var record in actual.Records.OrderBy(Key))
                {
                    builder.Append(record.EntityType).Append('\0')
                        .Append(record.SourceEntityId).Append('\0')
                        .Append(PortabilityImportFingerprint.Entity(record.Payload)).Append('\n');
                }
            }

            if (actual?.Summary.ValueKind == JsonValueKind.Object)
            {
                builder.Append(PortabilityImportFingerprint.Entity(actual.Summary));
            }

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        }
    }
}
