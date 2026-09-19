using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.Contractor;
using HomeBudget.Accounting.Api.Models.PaymentAccount;

namespace HomeBudget.Accounting.Api.Idempotency
{
    internal static class ReferenceCreateFingerprint
    {
        public static string Account(CreatePaymentAccountRequest request) => Hash(
            request.AccountType.ToString(CultureInfo.InvariantCulture),
            request.Currency?.Trim() ?? string.Empty,
            request.InitialBalance.ToString("G29", CultureInfo.InvariantCulture),
            request.Agent?.Trim() ?? string.Empty,
            request.Description?.Trim() ?? string.Empty,
            request.SourceReference?.Trim() ?? string.Empty);

        public static string Category(CreateCategoryRequest request) => Hash(
            request.CategoryType.ToString(CultureInfo.InvariantCulture),
            JoinNodes(request.NameNodes),
            request.SourceReference?.Trim() ?? string.Empty);

        public static string Contractor(CreateContractorRequest request) => Hash(
            JoinNodes(request.NameNodes),
            request.SourceReference?.Trim() ?? string.Empty);

        private static string JoinNodes(System.Collections.Generic.IEnumerable<string> nodes) =>
            string.Join("\u001f", nodes?.Select(static value => value?.Trim() ?? string.Empty) ?? []);

        private static string Hash(params string[] values)
        {
            var canonical = string.Join("|", values.Select(static value => $"{value.Length}:{value}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
    }
}
