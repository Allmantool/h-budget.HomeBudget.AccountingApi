using System;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Accounting.Infrastructure.DataProtection.Telemetry;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class DataProtectionOperationProcessor
    {
        private readonly IDataProtectionOperationStore _store;
        private readonly IDataProtectionOperationExecutor _executor;
        private readonly IDateTimeProvider _dateTimeProvider;

        public DataProtectionOperationProcessor(
            IDataProtectionOperationStore store,
            IDataProtectionOperationExecutor executor,
            IDateTimeProvider dateTimeProvider)
        {
            _store = store;
            _executor = executor;
            _dateTimeProvider = dateTimeProvider;
        }

        public async Task<Guid?> ProcessNextAsync(
            string ownerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

            var operation = await _store.GetNextRunnableAsync(cancellationToken);
            if (operation is null)
            {
                return null;
            }

            return await ProcessAsync(
                operation.OperationId,
                ownerId,
                leaseDuration,
                cancellationToken);
        }

        public async Task<Guid?> ProcessAsync(
            Guid operationId,
            string ownerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
            var operation = await _store.GetAsync(operationId, cancellationToken);
            if (operation is null || IsTerminal(operation))
            {
                return null;
            }

            var nowUtc = _dateTimeProvider.GetNowUtc();
            var lease = await _store.TryAcquireLeaseAsync(
                operation.OperationId,
                operation.ScopeKey,
                ownerId,
                nowUtc,
                nowUtc.Add(leaseDuration),
                cancellationToken);
            if (lease is null)
            {
                return null;
            }

            var fenceContext = lease.CreateFenceContext();
            using var leaseLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var leaseRenewal = MaintainLeaseAsync(
                fenceContext,
                leaseDuration,
                leaseLifetime.Token);
            try
            {
                var processing = ExecuteStateMachineAsync(operation, fenceContext, leaseLifetime.Token);
                var completed = await Task.WhenAny(processing, leaseRenewal);
                if (completed == leaseRenewal)
                {
                    await leaseLifetime.CancelAsync();
                    await leaseRenewal;
                }

                await processing;
                return operation.OperationId;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                DataProtectionTelemetry.RecordOperationFailure();
                await MarkFailedAsync(fenceContext);
                throw;
            }
            finally
            {
                await leaseLifetime.CancelAsync();
                await IgnoreExpectedLeaseCancellationAsync(leaseRenewal, leaseLifetime.Token);
                await _store.ReleaseLeaseAsync(fenceContext, CancellationToken.None);
            }
        }

        private async Task MaintainLeaseAsync(
            DataProtectionOperationFenceContext fenceContext,
            TimeSpan leaseDuration,
            CancellationToken leaseCancellationToken)
        {
            var renewalInterval = TimeSpan.FromTicks(Math.Max(
                TimeSpan.FromMilliseconds(50).Ticks,
                leaseDuration.Ticks / 3));

            while (true)
            {
                await Task.Delay(renewalInterval, leaseCancellationToken);
                var nowUtc = _dateTimeProvider.GetNowUtc();
                var renewed = await _store.RenewLeaseAsync(
                    fenceContext,
                    nowUtc,
                    nowUtc.Add(leaseDuration),
                    leaseCancellationToken);
                if (!renewed)
                {
                    throw new InvalidOperationException(
                        $"Data-protection lease '{fenceContext.LeaseId}' is no longer owned by this worker.");
                }
            }
        }

        private static async Task IgnoreExpectedLeaseCancellationAsync(
            Task leaseRenewal,
            CancellationToken cancellationToken)
        {
            try
            {
                await leaseRenewal;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task ExecuteStateMachineAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            operation = await CancelOrTransitionAsync(
                operation,
                DataProtectionOperationState.Queued,
                DataProtectionOperationState.Validating,
                fenceContext,
                cancellationToken);
            if (IsTerminal(operation))
            {
                return;
            }

            operation = await CancelOrTransitionAsync(
                operation,
                DataProtectionOperationState.Validating,
                DataProtectionOperationState.Running,
                fenceContext,
                cancellationToken);
            if (IsTerminal(operation))
            {
                return;
            }

            if (operation.State == DataProtectionOperationState.Running)
            {
                await _executor.ExecuteAsync(operation, fenceContext, cancellationToken);
                operation = await RefreshAsync(operation.OperationId, cancellationToken);
                operation = await CancelOrTransitionAsync(
                    operation,
                    DataProtectionOperationState.Running,
                    DataProtectionOperationState.AwaitingReconciliation,
                    fenceContext,
                    cancellationToken);
            }

            if (IsTerminal(operation))
            {
                return;
            }

            if (operation.State == DataProtectionOperationState.AwaitingReconciliation)
            {
                try
                {
                    await _executor.ReconcileAsync(operation, fenceContext, cancellationToken);
                }
                catch
                {
                    DataProtectionTelemetry.RecordReconciliationFailure();
                    throw;
                }

                operation = await RefreshAsync(operation.OperationId, cancellationToken);
                await CancelOrTransitionAsync(
                    operation,
                    DataProtectionOperationState.AwaitingReconciliation,
                    DataProtectionOperationState.Succeeded,
                    fenceContext,
                    cancellationToken);
            }
        }

        private async Task<DataProtectionOperation> CancelOrTransitionAsync(
            DataProtectionOperation operation,
            DataProtectionOperationState expectedState,
            DataProtectionOperationState nextState,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            if (operation.State != expectedState)
            {
                return operation;
            }

            var selectedState = operation.CancellationRequested
                ? DataProtectionOperationState.Cancelled
                : nextState;
            var transitioned = await _store.TryTransitionAsync(
                fenceContext,
                expectedState,
                selectedState,
                fenceContext.OwnerId,
                _dateTimeProvider.GetNowUtc(),
                cancellationToken);
            if (!transitioned)
            {
                throw new InvalidOperationException(
                    $"Data-protection lease fence '{fenceContext.FenceToken}' no longer owns operation " +
                    $"'{fenceContext.OperationId}'.");
            }

            return await RefreshAsync(operation.OperationId, cancellationToken);
        }

        private async Task MarkFailedAsync(DataProtectionOperationFenceContext fenceContext)
        {
            var operation = await _store.GetAsync(fenceContext.OperationId, CancellationToken.None);
            if (operation is null || IsTerminal(operation) || operation.State == DataProtectionOperationState.Queued)
            {
                return;
            }

            await _store.TryTransitionAsync(
                fenceContext,
                operation.State,
                DataProtectionOperationState.Failed,
                fenceContext.OwnerId,
                _dateTimeProvider.GetNowUtc(),
                CancellationToken.None);
        }

        private async Task<DataProtectionOperation> RefreshAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            return await _store.GetAsync(operationId, cancellationToken)
                ?? throw new InvalidOperationException($"Data-protection operation '{operationId}' no longer exists.");
        }

        private static bool IsTerminal(DataProtectionOperation operation)
        {
            return operation.State is DataProtectionOperationState.Succeeded
                or DataProtectionOperationState.Failed
                or DataProtectionOperationState.Cancelled;
        }
    }
}
