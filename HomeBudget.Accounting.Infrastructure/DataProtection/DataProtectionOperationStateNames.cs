using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal static class DataProtectionOperationStateNames
    {
        private static readonly Dictionary<DataProtectionOperationState, string> Names =
            new Dictionary<DataProtectionOperationState, string>
            {
                [DataProtectionOperationState.Queued] = "queued",
                [DataProtectionOperationState.Validating] = "validating",
                [DataProtectionOperationState.Running] = "running",
                [DataProtectionOperationState.AwaitingReconciliation] = "awaiting-reconciliation",
                [DataProtectionOperationState.Succeeded] = "succeeded",
                [DataProtectionOperationState.Failed] = "failed",
                [DataProtectionOperationState.Cancelled] = "cancelled"
            };

        public static string Format(DataProtectionOperationState state)
        {
            return Names[state];
        }

        public static DataProtectionOperationState Parse(string value)
        {
            foreach (var pair in Names)
            {
                if (string.Equals(pair.Value, value, StringComparison.Ordinal))
                {
                    return pair.Key;
                }
            }

            throw new InvalidOperationException($"Unknown data-protection operation state '{value}'.");
        }
    }
}
