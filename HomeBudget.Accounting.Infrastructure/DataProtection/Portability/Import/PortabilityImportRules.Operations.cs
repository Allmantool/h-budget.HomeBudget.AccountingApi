using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal static partial class PortabilityImportRules
    {
        private static void ValidateOperations(ValidationState state)
        {
            var nestedIdentities = new HashSet<string>(state.Operations.Keys, StringComparer.Ordinal);
            foreach (var operation in state.Operations.Values)
            {
                ValidateArchived(operation, "operations.jsonl");
                if (operation.GetProperty("kind").GetString() == "transfer")
                {
                    ValidateTransfer(state, operation, nestedIdentities);
                }
                else
                {
                    ValidateAccountOperation(state, operation, nestedIdentities);
                }
            }

            foreach (var account in state.Accounts.Values)
            {
                var opening = state.Operations[account.GetProperty("openingBalanceOperationId").GetString()];
                if (opening.GetProperty("kind").GetString() != "opening-balance"
                    || opening.GetProperty("accountId").GetString() != account.GetProperty("id").GetString())
                {
                    throw Reject(
                        "import.financial-invariant",
                        "accounts.jsonl",
                        account.GetProperty("id").GetString(),
                        "openingBalanceOperationId");
                }
            }
        }

        private static void ValidateAccountOperation(
            ValidationState state,
            JsonElement operation,
            HashSet<string> nestedIdentities)
        {
            var id = operation.GetProperty("id").GetString();
            var account = RequireValue(state.Accounts, operation, "accountId");
            var currency = RequireValue(state.Currencies, operation, "currencyId");
            if (account.GetProperty("currencyId").GetString() != currency.GetProperty("id").GetString())
            {
                throw Reject("import.financial-invariant", "operations.jsonl", id, "currencyId");
            }

            ValidateAmount(operation.GetProperty("amount").GetString(), currency, id, "amount");
            RequireOptional(state.Categories, operation, "operations.jsonl", "categoryId");
            RequireOptional(state.Contractors, operation, "operations.jsonl", "contractorId");
            RequireOptional(state.Lineage, operation, "operations.jsonl", "lineageId");
            var kind = operation.GetProperty("kind").GetString();
            var direction = operation.GetProperty("direction").GetString();
            if (((kind == "opening-balance" || kind == "income") && direction != "credit")
                || (kind == "expense" && direction != "debit"))
            {
                throw Reject("import.financial-invariant", "operations.jsonl", id, "direction");
            }

            decimal splitTotal = 0;
            var splitCount = 0;
            foreach (var split in operation.GetProperty("splits").EnumerateArray())
            {
                AddNestedIdentity(nestedIdentities, split, id, "splits.id");
                Require(state.Categories, split, "operations.jsonl", "categoryId");
                ValidateAmount(split.GetProperty("amount").GetString(), currency, id, "splits.amount");
                splitTotal += Decimal(split.GetProperty("amount").GetString(), id, "splits.amount");
                splitCount++;
            }

            if (splitCount > 0
                && splitTotal != Decimal(operation.GetProperty("amount").GetString(), id, "amount"))
            {
                throw Reject("import.split-invariant", "operations.jsonl", id, "splits");
            }
        }

        private static void ValidateTransfer(
            ValidationState state,
            JsonElement operation,
            HashSet<string> nestedIdentities)
        {
            var id = operation.GetProperty("id").GetString();
            var source = operation.GetProperty("source");
            var destination = operation.GetProperty("destination");
            AddNestedIdentity(nestedIdentities, source, id, "source.id");
            AddNestedIdentity(nestedIdentities, destination, id, "destination.id");
            var sourceCurrency = ValidateTransferSide(state, source, id, "source", "debit");
            var destinationCurrency = ValidateTransferSide(state, destination, id, "destination", "credit");
            if (source.GetProperty("accountId").GetString() == destination.GetProperty("accountId").GetString())
            {
                throw Reject("import.transfer-invariant", "operations.jsonl", id, "destination.accountId");
            }

            var exchange = operation.GetProperty("exchangeRate");
            if (exchange.ValueKind != JsonValueKind.Null)
            {
                ValidateExchangeRate(state, exchange, source, destination, sourceCurrency, destinationCurrency, id);
            }

            var fee = operation.GetProperty("fee");
            if (fee.ValueKind != JsonValueKind.Null)
            {
                AddNestedIdentity(nestedIdentities, fee, id, "fee.id");
                var feeCurrency = ValidateTransferSide(state, fee, id, "fee", "debit");
                Require(state.Categories, fee, "operations.jsonl", "categoryId");
                if (fee.GetProperty("accountId").GetString() != source.GetProperty("accountId").GetString()
                    || feeCurrency.GetProperty("id").GetString() != sourceCurrency.GetProperty("id").GetString())
                {
                    throw Reject("import.transfer-invariant", "operations.jsonl", id, "fee");
                }
            }

            RequireOptional(state.Lineage, operation, "operations.jsonl", "lineageId");
        }

        private static JsonElement ValidateTransferSide(
            ValidationState state,
            JsonElement side,
            string operationId,
            string field,
            string direction)
        {
            var account = RequireValue(state.Accounts, side, "accountId", operationId, $"{field}.accountId");
            var currency = RequireValue(state.Currencies, side, "currencyId", operationId, $"{field}.currencyId");
            if (side.GetProperty("direction").GetString() != direction
                || account.GetProperty("currencyId").GetString() != currency.GetProperty("id").GetString())
            {
                throw Reject("import.transfer-invariant", "operations.jsonl", operationId, field);
            }

            ValidateAmount(side.GetProperty("amount").GetString(), currency, operationId, $"{field}.amount");
            return currency;
        }

        private static void ValidateExchangeRate(
            ValidationState state,
            JsonElement exchange,
            JsonElement source,
            JsonElement destination,
            JsonElement sourceCurrency,
            JsonElement destinationCurrency,
            string operationId)
        {
            var value = Decimal(exchange.GetProperty("value").GetString(), operationId, "exchangeRate.value");
            if (value <= 0
                || Decimal(source.GetProperty("amount").GetString(), operationId, "source.amount") * value
                    != Decimal(destination.GetProperty("amount").GetString(), operationId, "destination.amount"))
            {
                throw Reject("import.transfer-invariant", "operations.jsonl", operationId, "exchangeRate.value");
            }

            if (exchange.GetProperty("rateId").ValueKind == JsonValueKind.String)
            {
                var rate = RequireValue(state.Rates, exchange, "rateId", operationId, "exchangeRate.rateId");
                if (rate.GetProperty("baseCurrencyId").GetString() != sourceCurrency.GetProperty("id").GetString()
                    || rate.GetProperty("quoteCurrencyId").GetString() != destinationCurrency.GetProperty("id").GetString()
                    || Decimal(rate.GetProperty("rate").GetString(), operationId, "exchangeRate.rateId") != value)
                {
                    throw Reject("import.transfer-invariant", "operations.jsonl", operationId, "exchangeRate.rateId");
                }
            }
        }

        private static JsonElement RequireValue(
            IReadOnlyDictionary<string, JsonElement> target,
            JsonElement source,
            string field,
            string entityId = null,
            string errorField = null)
        {
            var id = source.GetProperty(field).GetString();
            if (!target.TryGetValue(id, out var value))
            {
                throw Reject(
                    "import.reference-missing",
                    "operations.jsonl",
                    entityId ?? source.GetProperty("id").GetString(),
                    errorField ?? field);
            }

            return value;
        }

        private static void ValidateAmount(string value, JsonElement currency, string entityId, string field)
        {
            var amount = Decimal(value, entityId, field);
            var separator = value.IndexOf('.', StringComparison.Ordinal);
            var scale = separator < 0 ? 0 : value.Length - separator - 1;
            if (amount <= 0 || scale > currency.GetProperty("minorUnit").GetInt32())
            {
                throw Reject("import.currency-precision", "operations.jsonl", entityId, field);
            }
        }

        private static decimal Decimal(string value, string entityId, string field)
        {
            if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result))
            {
                throw Reject("import.financial-invariant", "operations.jsonl", entityId, field);
            }

            return result;
        }

        private static void AddNestedIdentity(
            HashSet<string> identities,
            JsonElement value,
            string operationId,
            string field)
        {
            if (!identities.Add(value.GetProperty("id").GetString()))
            {
                throw Reject("import.identity-duplicate", "operations.jsonl", operationId, field);
            }
        }

        private static void ValidateArchived(JsonElement value, string path)
        {
            var archived = value.GetProperty("archived").GetBoolean();
            var timestamp = value.GetProperty("archivedUtc").ValueKind != JsonValueKind.Null;
            if (archived != timestamp)
            {
                throw Reject(
                    "import.financial-invariant",
                    path,
                    value.GetProperty("id").GetString(),
                    "archivedUtc");
            }
        }
    }
}
