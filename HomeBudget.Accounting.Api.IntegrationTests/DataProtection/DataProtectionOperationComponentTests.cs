using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    public sealed class DataProtectionOperationComponentTests : BaseIntegrationTests
    {
        private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);

        [Test]
        public async Task Restart_WhenNewStoreAndProcessorAreCreated_ResumesTheSameOperationId()
        {
            var request = DataProtectionOperationTestFactory.CreateRequest();
            var firstStore = CreateStore();
            await firstStore.EnqueueAsync(request, CancellationToken.None);
            var firstLease = await AcquireLeaseAsync(firstStore, request, "first-worker");
            Assert.That(
                await firstStore.TryTransitionAsync(
                    firstLease.CreateFenceContext(),
                    DataProtectionOperationState.Queued,
                    DataProtectionOperationState.Validating,
                    "first-worker",
                    DateTime.UtcNow,
                    CancellationToken.None),
                Is.True);
            await firstStore.ReleaseLeaseAsync(firstLease.CreateFenceContext(), CancellationToken.None);

            var resumedExecutor = new ControllableDataProtectionOperationExecutor();
            var restartedProcessor = DataProtectionOperationTestFactory.CreateProcessor(
                CreateStore(),
                resumedExecutor);
            var processedId = await restartedProcessor.ProcessAsync(
                request.OperationId,
                "restarted-worker",
                LeaseDuration,
                CancellationToken.None);

            var settled = await CreateStore().GetAsync(request.OperationId, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(processedId, Is.EqualTo(request.OperationId));
                Assert.That(settled.State, Is.EqualTo(DataProtectionOperationState.Succeeded));
                Assert.That(resumedExecutor.ExecutedOperationIds, Is.EqualTo(new[] { request.OperationId }));
                Assert.That(resumedExecutor.ReconciledOperationIds, Is.EqualTo(new[] { request.OperationId }));
            });
        }

        [Test]
        public async Task Transition_WhenRequested_IsDurableAndRestrictedToTheLegalStateGraph()
        {
            var request = DataProtectionOperationTestFactory.CreateRequest();
            var firstStore = CreateStore();
            await firstStore.EnqueueAsync(request, CancellationToken.None);
            var lease = await AcquireLeaseAsync(firstStore, request, "worker-one");
            var fenceContext = lease.CreateFenceContext();

            Assert.That(
                await firstStore.TryTransitionAsync(
                    fenceContext,
                    DataProtectionOperationState.Queued,
                    DataProtectionOperationState.Validating,
                    "worker-one",
                    DateTime.UtcNow,
                    CancellationToken.None),
                Is.True);

            var restartedStore = CreateStore();
            var durable = await restartedStore.GetAsync(request.OperationId, CancellationToken.None);
            Assert.That(durable.State, Is.EqualTo(DataProtectionOperationState.Validating));
            Assert.That(
                async () => await restartedStore.TryTransitionAsync(
                    fenceContext,
                    DataProtectionOperationState.Validating,
                    DataProtectionOperationState.Succeeded,
                    "worker-two",
                    DateTime.UtcNow,
                    CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                await restartedStore.TryTransitionAsync(
                    fenceContext,
                    DataProtectionOperationState.Queued,
                    DataProtectionOperationState.Validating,
                    "stale-worker",
                    DateTime.UtcNow,
                    CancellationToken.None),
                Is.False);
            Assert.That(
                (await restartedStore.GetAsync(request.OperationId, CancellationToken.None)).State,
                Is.EqualTo(DataProtectionOperationState.Validating));
            await firstStore.ReleaseLeaseAsync(fenceContext, CancellationToken.None);
        }

        [Test]
        public async Task Cancellation_WhenRequestedDuringExecution_IsHonoredAtTheNextSafeCheckpoint()
        {
            var request = DataProtectionOperationTestFactory.CreateRequest();
            var store = CreateStore();
            await store.EnqueueAsync(request, CancellationToken.None);
            var executor = new ControllableDataProtectionOperationExecutor(blockExecution: true);
            var processing = DataProtectionOperationTestFactory.CreateProcessor(store, executor)
                .ProcessAsync(
                    request.OperationId,
                    "cancellable-worker",
                    LeaseDuration,
                    CancellationToken.None);
            await executor.ExecutionEntered.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(
                await CreateStore().RequestCancellationAsync(
                    request.OperationId,
                    "administrator",
                    DateTime.UtcNow,
                    CancellationToken.None),
                Is.True);
            executor.AllowExecutionToComplete();
            await processing;

            var cancelled = await CreateStore().GetAsync(request.OperationId, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(cancelled.State, Is.EqualTo(DataProtectionOperationState.Cancelled));
                Assert.That(executor.ExecuteCount, Is.EqualTo(1));
                Assert.That(executor.ReconcileCount, Is.Zero);
            });
        }

        [Test]
        public async Task Lease_WhenAnotherReplicaOwnsTheScope_ExcludesConcurrentProcessing()
        {
            var request = DataProtectionOperationTestFactory.CreateRequest();
            var firstStore = CreateStore();
            await firstStore.EnqueueAsync(request, CancellationToken.None);
            var firstExecutor = new ControllableDataProtectionOperationExecutor(blockExecution: true);
            var firstProcessing = DataProtectionOperationTestFactory.CreateProcessor(firstStore, firstExecutor)
                .ProcessAsync(
                    request.OperationId,
                    "replica-one",
                    TimeSpan.FromMilliseconds(600),
                    CancellationToken.None);
            await firstExecutor.ExecutionEntered.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(900));

            var secondExecutor = new ControllableDataProtectionOperationExecutor();
            var secondResult = await DataProtectionOperationTestFactory.CreateProcessor(
                    CreateStore(),
                    secondExecutor)
                .ProcessAsync(
                    request.OperationId,
                    "replica-two",
                    LeaseDuration,
                    CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(secondResult, Is.Null);
                Assert.That(secondExecutor.ExecuteCount, Is.Zero);
            });

            firstExecutor.AllowExecutionToComplete();
            await firstProcessing;
            Assert.That(
                (await CreateStore().GetAsync(request.OperationId, CancellationToken.None)).State,
                Is.EqualTo(DataProtectionOperationState.Succeeded));
        }

        [Test]
        public async Task Lease_WhenExpiredLeaseIsTakenOver_StaleOwnerCannotFailOrContinueTheOperation()
        {
            var request = DataProtectionOperationTestFactory.CreateRequest();
            var firstStore = CreateStore();
            await firstStore.EnqueueAsync(request, CancellationToken.None);
            var staleExecutor = new ControllableDataProtectionOperationExecutor(blockExecution: true);
            var staleProcessing = DataProtectionOperationTestFactory.CreateProcessor(firstStore, staleExecutor)
                .ProcessAsync(
                    request.OperationId,
                    "stale-replica",
                    TimeSpan.FromMilliseconds(600),
                    CancellationToken.None);
            await staleExecutor.ExecutionEntered.WaitAsync(TimeSpan.FromSeconds(10));

            var takeoverNow = DateTime.UtcNow.AddMinutes(1);
            var takeoverLease = await CreateStore().TryAcquireLeaseAsync(
                request.OperationId,
                request.ScopeKey,
                "takeover-replica",
                takeoverNow,
                takeoverNow.Add(LeaseDuration),
                CancellationToken.None);
            Assert.That(takeoverLease, Is.Not.Null);

            Assert.That(
                async () => await staleProcessing,
                Throws.TypeOf<InvalidOperationException>());
            var operationAfterTakeover = await CreateStore().GetAsync(
                request.OperationId,
                CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(staleExecutor.ExecutionCancelled, Is.True);
                Assert.That(staleExecutor.ReconcileCount, Is.Zero);
                Assert.That(operationAfterTakeover.State, Is.EqualTo(DataProtectionOperationState.Running));
                Assert.That(staleExecutor.FenceContexts, Has.Length.EqualTo(1));
                Assert.That(staleExecutor.FenceContexts[0].FenceToken, Is.LessThan(takeoverLease.FenceToken));
            });

            await CreateStore().ReleaseLeaseAsync(
                takeoverLease.CreateFenceContext(),
                CancellationToken.None);
            var resumedExecutor = new ControllableDataProtectionOperationExecutor();
            var resumed = await DataProtectionOperationTestFactory.CreateProcessor(CreateStore(), resumedExecutor)
                .ProcessAsync(
                    request.OperationId,
                    "replacement-replica",
                    LeaseDuration,
                    CancellationToken.None);
            var settled = await CreateStore().GetAsync(request.OperationId, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(resumed, Is.EqualTo(request.OperationId));
                Assert.That(resumedExecutor.ExecuteCount, Is.EqualTo(1));
                Assert.That(settled.State, Is.EqualTo(DataProtectionOperationState.Succeeded));
            });
        }

        [Test]
        public async Task Selection_WhenOldestScopeIsLeased_ProcessesAnotherRunnableScope()
        {
            var oldestUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var firstRequest = DataProtectionOperationTestFactory.CreateRequest(oldestUtc);
            var store = CreateStore();
            await store.EnqueueAsync(firstRequest, CancellationToken.None);
            var firstExecutor = new ControllableDataProtectionOperationExecutor(blockExecution: true);
            var firstProcessing = DataProtectionOperationTestFactory.CreateProcessor(store, firstExecutor)
                .ProcessAsync(
                    firstRequest.OperationId,
                    "scope-a-replica",
                    TimeSpan.FromMilliseconds(600),
                    CancellationToken.None);
            await firstExecutor.ExecutionEntered.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(900));

            var secondRequest = DataProtectionOperationTestFactory.CreateRequest(oldestUtc.AddSeconds(1));
            await CreateStore().EnqueueAsync(secondRequest, CancellationToken.None);
            var secondExecutor = new ControllableDataProtectionOperationExecutor();
            var secondResult = await DataProtectionOperationTestFactory.CreateProcessor(
                    CreateStore(),
                    secondExecutor)
                .ProcessNextAsync("scope-b-replica", LeaseDuration, CancellationToken.None);
            firstExecutor.AllowExecutionToComplete();
            await firstProcessing;

            Assert.Multiple(() =>
            {
                Assert.That(secondResult, Is.EqualTo(secondRequest.OperationId));
                Assert.That(secondExecutor.ExecutedOperationIds, Is.EqualTo(new[] { secondRequest.OperationId }));
            });
        }

        [Test]
        public async Task StatusPolling_WhenStateChanges_ReturnsTheDurableUpdatedStatus()
        {
            var request = DataProtectionOperationTestFactory.CreateRequest();
            var store = CreateStore();
            var queued = await store.EnqueueAsync(request, CancellationToken.None);
            var lease = await AcquireLeaseAsync(store, request, "polling-worker");
            var polling = CreateStore().WaitForChangeAsync(
                request.OperationId,
                queued.RowVersion,
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(25),
                CancellationToken.None);

            await store.TryTransitionAsync(
                lease.CreateFenceContext(),
                DataProtectionOperationState.Queued,
                DataProtectionOperationState.Validating,
                "polling-worker",
                DateTime.UtcNow,
                CancellationToken.None);
            var changed = await polling;

            Assert.That(changed, Is.Not.Null);
            Assert.That(changed.State, Is.EqualTo(DataProtectionOperationState.Validating));
            await store.ReleaseLeaseAsync(lease.CreateFenceContext(), CancellationToken.None);
        }

        [Test]
        public async Task StatusPolling_WhenNoStateChanges_ReturnsWithinTheConfiguredBound()
        {
            var request = DataProtectionOperationTestFactory.CreateRequest();
            var store = CreateStore();
            var queued = await store.EnqueueAsync(request, CancellationToken.None);
            var stopwatch = Stopwatch.StartNew();

            var changed = await store.WaitForChangeAsync(
                request.OperationId,
                queued.RowVersion,
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(25),
                CancellationToken.None);
            stopwatch.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(changed, Is.Null);
                Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150)));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            });
        }

        private SqlDataProtectionOperationStore CreateStore()
        {
            return new SqlDataProtectionOperationStore(TestContainers.AccountingDbConnectionString);
        }

        private static async Task<DataProtectionLease> AcquireLeaseAsync(
            SqlDataProtectionOperationStore store,
            DataProtectionOperationRequest request,
            string ownerId)
        {
            var nowUtc = DateTime.UtcNow;
            return await store.TryAcquireLeaseAsync(
                request.OperationId,
                request.ScopeKey,
                ownerId,
                nowUtc,
                nowUtc.Add(LeaseDuration),
                CancellationToken.None);
        }
    }
}
