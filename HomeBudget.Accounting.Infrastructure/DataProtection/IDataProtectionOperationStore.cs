using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal interface IDataProtectionOperationStore
    {
        Task<DataProtectionOperation> EnqueueAsync(
            DataProtectionOperationRequest request,
            CancellationToken cancellationToken);

        Task<DataProtectionOperation> GetAsync(Guid operationId, CancellationToken cancellationToken);

        Task<DataProtectionOperation> GetNextRunnableAsync(CancellationToken cancellationToken);

        Task<bool> TryTransitionAsync(
            DataProtectionOperationFenceContext fenceContext,
            DataProtectionOperationState expectedState,
            DataProtectionOperationState nextState,
            string actorId,
            DateTime nowUtc,
            CancellationToken cancellationToken);

        Task<bool> RequestCancellationAsync(
            Guid operationId,
            string actorId,
            DateTime nowUtc,
            CancellationToken cancellationToken);

        Task<DataProtectionLease> TryAcquireLeaseAsync(
            Guid operationId,
            string scopeKey,
            string ownerId,
            DateTime nowUtc,
            DateTime expiresUtc,
            CancellationToken cancellationToken);

        Task<bool> RenewLeaseAsync(
            DataProtectionOperationFenceContext fenceContext,
            DateTime nowUtc,
            DateTime expiresUtc,
            CancellationToken cancellationToken);

        Task ReleaseLeaseAsync(
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken);

        Task<DataProtectionOperation> WaitForChangeAsync(
            Guid operationId,
            byte[] knownRowVersion,
            TimeSpan timeout,
            TimeSpan pollInterval,
            CancellationToken cancellationToken);
    }
}
