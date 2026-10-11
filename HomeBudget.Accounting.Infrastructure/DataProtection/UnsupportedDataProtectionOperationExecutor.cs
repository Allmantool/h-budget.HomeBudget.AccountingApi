using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class UnsupportedDataProtectionOperationExecutor : IDataProtectionOperationExecutor
    {
        public System.Collections.Generic.IReadOnlyCollection<string> SupportedOperationKinds =>
            System.Array.Empty<string>();

        public Task ExecuteAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException(
                $"No executor is registered for data-protection operation kind '{operation.OperationKind}'.");
        }

        public Task ReconcileAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException(
                $"No reconciler is registered for data-protection operation kind '{operation.OperationKind}'.");
        }
    }
}
