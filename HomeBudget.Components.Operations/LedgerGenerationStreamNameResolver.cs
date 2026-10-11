using System;
using System.Text;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;

namespace HomeBudget.Components.Operations
{
    public static class LedgerGenerationStreamNameResolver
    {
        public static string Resolve(PaymentOperationEvent paymentEvent, string legacyStreamName)
        {
            var ledgerId = paymentEvent.Metadata.Get(EventMetadataKeys.LedgerId);
            var generationId = paymentEvent.Metadata.Get(EventMetadataKeys.LedgerGenerationId);
            if ((string.IsNullOrWhiteSpace(ledgerId) || ledgerId == "default")
                && (string.IsNullOrWhiteSpace(generationId) || generationId == "legacy"))
            {
                return legacyStreamName;
            }

            return $"ledger~{Encode(ledgerId)}~generation~{Encode(generationId)}~{legacyStreamName}";
        }

        private static string Encode(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
