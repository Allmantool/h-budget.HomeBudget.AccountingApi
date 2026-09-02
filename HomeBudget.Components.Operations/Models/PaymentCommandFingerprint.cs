using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Components.Operations.Models
{
    public static class PaymentCommandFingerprint
    {
        public static string HashIdempotencyKey(string idempotencyKey)
        {
            ArgumentNullException.ThrowIfNull(idempotencyKey);
            return Hash(idempotencyKey.Trim());
        }

        public static string Create(
            string commandType,
            Guid paymentAccountId,
            Guid? targetOperationId,
            PaymentOperationPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            var canonical = JoinCanonical(
                commandType,
                paymentAccountId.ToString("D"),
                targetOperationId?.ToString("D") ?? string.Empty,
                payload.Amount.ToString("G29", CultureInfo.InvariantCulture),
                NormalizeGuid(payload.CategoryId),
                NormalizeGuid(payload.ContractorId),
                payload.OperationDate == default
                    ? string.Empty
                    : payload.OperationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                payload.ScopeOperationId.ToString(CultureInfo.InvariantCulture),
                payload.Comment ?? string.Empty);

            return Hash(canonical);
        }

        public static string CreateDelete(string commandType, Guid paymentAccountId, Guid operationId)
        {
            return Hash(JoinCanonical(commandType, paymentAccountId.ToString("D"), operationId.ToString("D")));
        }

        public static string CreateDerivedIdempotencyKeyHash(string parentKeyHash, string purpose)
        {
            return Hash($"{parentKeyHash}|{purpose}");
        }

        private static string NormalizeGuid(string value)
        {
            return Guid.TryParse(value, out var guid) ? guid.ToString("D") : value?.Trim() ?? string.Empty;
        }

        private static string JoinCanonical(params string[] values)
        {
            return string.Join("|", values.Select(static value => $"{value.Length}:{value}"));
        }

        private static string Hash(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }
    }
}
