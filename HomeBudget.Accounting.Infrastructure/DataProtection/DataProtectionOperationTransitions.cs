using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal static class DataProtectionOperationTransitions
    {
        private static readonly Dictionary<DataProtectionOperationState, DataProtectionOperationState[]> Allowed =
            new Dictionary<DataProtectionOperationState, DataProtectionOperationState[]>
            {
                [DataProtectionOperationState.Queued] =
                    [DataProtectionOperationState.Validating, DataProtectionOperationState.Cancelled],
                [DataProtectionOperationState.Validating] =
                    [DataProtectionOperationState.Running, DataProtectionOperationState.Failed, DataProtectionOperationState.Cancelled],
                [DataProtectionOperationState.Running] =
                    [DataProtectionOperationState.AwaitingReconciliation, DataProtectionOperationState.Failed, DataProtectionOperationState.Cancelled],
                [DataProtectionOperationState.AwaitingReconciliation] =
                    [DataProtectionOperationState.Succeeded, DataProtectionOperationState.Failed, DataProtectionOperationState.Cancelled],
                [DataProtectionOperationState.Succeeded] = [],
                [DataProtectionOperationState.Failed] = [],
                [DataProtectionOperationState.Cancelled] = []
            };

        public static void EnsureAllowed(
            DataProtectionOperationState expectedState,
            DataProtectionOperationState nextState)
        {
            if (Array.IndexOf(Allowed[expectedState], nextState) >= 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Data-protection operation transition '{expectedState}' -> '{nextState}' is not allowed.");
        }
    }
}
