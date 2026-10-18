using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed class CommonCutoffCalculator
    {
        public DateTimeOffset Calculate(
            IReadOnlyCollection<CaptureComponentReceipt> receipts,
            IReadOnlyCollection<string> requiredComponents)
        {
            ArgumentNullException.ThrowIfNull(receipts);
            ArgumentNullException.ThrowIfNull(requiredComponents);
            if (requiredComponents.Count == 0)
            {
                throw new InvalidOperationException("At least one capture component is required.");
            }

            var required = new HashSet<string>(requiredComponents, StringComparer.Ordinal);
            if (required.Count != requiredComponents.Count)
            {
                throw new InvalidOperationException("Required capture components must be unique.");
            }

            var byComponent = receipts
                .Where(receipt => required.Contains(receipt.ComponentName))
                .GroupBy(receipt => receipt.ComponentName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            foreach (var component in required)
            {
                if (!byComponent.TryGetValue(component, out var componentReceipts)
                    || componentReceipts.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Capture component '{component}' must provide exactly one durable receipt.");
                }

                ValidateReceipt(componentReceipts[0]);
            }

            return byComponent.Values.Min(receipt => receipt[0].CommittedUtc);
        }

        private static void ValidateReceipt(CaptureComponentReceipt receipt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(receipt.ComponentName);
            ArgumentException.ThrowIfNullOrWhiteSpace(receipt.ReceiptKind);
            ArgumentOutOfRangeException.ThrowIfNegative(receipt.ProtectionSequence);
            if (receipt.CommittedUtc.Offset != TimeSpan.Zero)
            {
                throw new InvalidOperationException("Component receipt timestamps must be UTC.");
            }

            using var _ = JsonDocument.Parse(receipt.WatermarkJson);
        }
    }
}
