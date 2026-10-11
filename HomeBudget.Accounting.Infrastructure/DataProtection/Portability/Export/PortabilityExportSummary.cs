using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    internal sealed class PortabilityExportSummary : IDisposable
    {
        private readonly IncrementalHash _operationIdentities = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long Accounts { get; private set; }

        public long Currencies { get; private set; }

        public long Categories { get; private set; }

        public long Contractors { get; private set; }

        public long Settings { get; set; }

        public long Operations { get; private set; }

        public long OpeningBalances { get; private set; }

        public long IncomeOperations { get; private set; }

        public long ExpenseOperations { get; private set; }

        public long AdjustmentOperations { get; private set; }

        public long TransferOperations { get; private set; }

        public long SplitLines { get; private set; }

        public long Rates { get; private set; }

        public long Lineage { get; private set; }

        public HashSet<string> AccountIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> CurrencyIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> CategoryIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ContractorIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> RateIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> LineageIds { get; } = new(StringComparer.Ordinal);

        public string OperationIdentitySha256 { get; private set; }

        public void ObserveAccount(JsonElement value)
        {
            Accounts++;
            Add(AccountIds, value, "id");
        }

        public void ObserveCurrency(JsonElement value)
        {
            Currencies++;
            Add(CurrencyIds, value, "id");
        }

        public void ObserveCategory(JsonElement value)
        {
            Categories++;
            Add(CategoryIds, value, "id");
        }

        public void ObserveContractor(JsonElement value)
        {
            Contractors++;
            Add(ContractorIds, value, "id");
        }

        public void ObserveRate(JsonElement value)
        {
            Rates++;
            Add(RateIds, value, "id");
        }

        public void ObserveLineage(JsonElement value)
        {
            Lineage++;
            Add(LineageIds, value, "id");
        }

        public void ObserveOperation(JsonElement value)
        {
            Operations++;
            var id = value.GetProperty("id").GetString();
            _operationIdentities.AppendData(Encoding.UTF8.GetBytes(id));
            _operationIdentities.AppendData("\n"u8);
            var kind = value.GetProperty("kind").GetString();
            switch (kind)
            {
                case "opening-balance": OpeningBalances++; break;
                case "income": IncomeOperations++; break;
                case "expense": ExpenseOperations++; break;
                case "adjustment": AdjustmentOperations++; break;
                case "transfer": TransferOperations++; break;
            }

            if (value.TryGetProperty("splits", out var splits))
            {
                SplitLines += splits.GetArrayLength();
            }
        }

        public void CaptureOperationDependencies(JsonElement operation)
        {
            Add(AccountIds, operation, "accountId");
            Add(CurrencyIds, operation, "currencyId");
            Add(CategoryIds, operation, "categoryId");
            Add(ContractorIds, operation, "contractorId");
            Add(LineageIds, operation, "lineageId");
            if (operation.TryGetProperty("splits", out var splits))
            {
                foreach (var split in splits.EnumerateArray())
                {
                    Add(CategoryIds, split, "categoryId");
                }
            }

            if (operation.TryGetProperty("source", out var source))
            {
                CaptureTransferSide(source);
                CaptureTransferSide(operation.GetProperty("destination"));
                var exchangeRate = operation.GetProperty("exchangeRate");
                Add(RateIds, exchangeRate, "rateId");
                if (operation.TryGetProperty("fee", out var fee) && fee.ValueKind == JsonValueKind.Object)
                {
                    CaptureTransferSide(fee);
                    Add(CategoryIds, fee, "categoryId");
                }
            }
        }

        public void Complete()
        {
            OperationIdentitySha256 = Convert.ToHexStringLower(_operationIdentities.GetHashAndReset());
        }

        public void Dispose() => _operationIdentities.Dispose();

        private void CaptureTransferSide(JsonElement side)
        {
            Add(AccountIds, side, "accountId");
            Add(CurrencyIds, side, "currencyId");
        }

        private static void Add(HashSet<string> target, JsonElement value, string propertyName)
        {
            if (value.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                target.Add(property.GetString());
            }
        }
    }
}
