using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Components.Operations.Models
{
    public static class TransferCommandFingerprint
    {
        public static string Create(CrossAccountsTransferPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            var values = new[]
            {
                payload.Sender.ToString("D"),
                payload.Recipient.ToString("D"),
                Amount(payload.SenderAmount ?? payload.Amount),
                Amount(payload.RecipientAmount ?? payload.Amount * (payload.CustomConversionMultiplier ?? payload.Multiplier)),
                payload.SenderCurrency?.Trim() ?? string.Empty,
                payload.RecipientCurrency?.Trim() ?? string.Empty,
                payload.OperationAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                payload.SourceReference?.Trim() ?? string.Empty
            };
            var canonical = string.Join("|", values.Select(static value => $"{value.Length}:{value}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        private static string Amount(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    }
}
