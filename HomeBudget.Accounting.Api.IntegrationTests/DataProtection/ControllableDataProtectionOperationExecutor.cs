using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal sealed class ControllableDataProtectionOperationExecutor : IDataProtectionOperationExecutor
    {
        public System.Collections.Generic.IReadOnlyCollection<string> SupportedOperationKinds =>
            DataProtectionOperationKinds.All;

        private readonly TaskCompletionSource _executionEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _executionRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _blockExecution;
        private readonly ConcurrentQueue<Guid> _executedOperationIds = new();
        private readonly ConcurrentQueue<Guid> _reconciledOperationIds = new();
        private readonly ConcurrentQueue<DataProtectionOperationFenceContext> _fenceContexts = new();
        private int _executeCount;
        private int _reconcileCount;
        private int _executionCancelled;

        public ControllableDataProtectionOperationExecutor(bool blockExecution = false)
        {
            _blockExecution = blockExecution;
        }

        public Task ExecutionEntered => _executionEntered.Task;
        public int ExecuteCount => _executeCount;
        public int ReconcileCount => _reconcileCount;
        public bool ExecutionCancelled => Volatile.Read(ref _executionCancelled) == 1;
        public Guid[] ExecutedOperationIds => _executedOperationIds.ToArray();
        public Guid[] ReconciledOperationIds => _reconciledOperationIds.ToArray();
        public DataProtectionOperationFenceContext[] FenceContexts => _fenceContexts.ToArray();

        public async Task ExecuteAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executeCount);
            _executedOperationIds.Enqueue(operation.OperationId);
            _fenceContexts.Enqueue(fenceContext);
            _executionEntered.TrySetResult();
            if (_blockExecution)
            {
                try
                {
                    await _executionRelease.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Exchange(ref _executionCancelled, 1);
                    throw;
                }
            }
        }

        public Task ReconcileAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reconcileCount);
            _reconciledOperationIds.Enqueue(operation.OperationId);
            return Task.CompletedTask;
        }

        public void AllowExecutionToComplete()
        {
            _executionRelease.TrySetResult();
        }
    }
}
