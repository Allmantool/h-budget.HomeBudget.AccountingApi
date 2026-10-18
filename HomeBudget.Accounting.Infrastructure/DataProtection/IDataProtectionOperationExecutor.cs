using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public interface IDataProtectionOperationExecutor
    {
        IReadOnlyCollection<string> SupportedOperationKinds { get; }

        Task ExecuteAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken);

        Task ReconcileAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken);
    }
}
