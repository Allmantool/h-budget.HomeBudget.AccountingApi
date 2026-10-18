using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.DataProtection.Triggers;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Triggers
{
    [TestFixture]
    public sealed class RecoveryTriggerCoordinatorTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

        [Test]
        public void Defaults_MatchTriggerContract()
        {
            var options = new RecoveryTriggerOptions();

            options.ScheduledCadence.Should().Be(TimeSpan.FromMinutes(30));
            options.MaximumCoalescingDelay.Should().BePositive();
            options.RecordThreshold.Should().BePositive();
            options.ByteThreshold.Should().BePositive();
        }

        [Test]
        public async Task RecordChangesAsync_BelowThresholdPersistsAcrossCoordinatorRestartAndTriggersAtBound()
        {
            var store = new RecordingTriggerStore();
            var options = new RecoveryTriggerOptions
            {
                RecordThreshold = 100,
                ByteThreshold = 1000,
                MaximumCoalescingDelay = TimeSpan.FromMinutes(10)
            };
            var first = new RecoveryTriggerCoordinator(store, new RecoveryTriggerPolicy(options));

            var coalesced = await first.RecordChangesAsync(Target, 1, 10, Now, CancellationToken.None);
            var restarted = new RecoveryTriggerCoordinator(store, new RecoveryTriggerPolicy(options));
            var bounded = await restarted.RecordChangesAsync(
                Target,
                0,
                0,
                Now.AddMinutes(10),
                CancellationToken.None);

            coalesced.WasTriggered.Should().BeFalse();
            bounded.WasTriggered.Should().BeTrue();
            bounded.TriggerReason.Should().Contain("bounded-coalescing");
            store.Dispatches.Should().ContainSingle();
        }

        [Test]
        public async Task RecordChangesAsync_WhenRecordOrByteThresholdIsReached_TriggersOnceWithCounts()
        {
            var store = new RecordingTriggerStore();
            var options = new RecoveryTriggerOptions { RecordThreshold = 2, ByteThreshold = 50 };
            var sut = new RecoveryTriggerCoordinator(store, new RecoveryTriggerPolicy(options));

            await sut.RecordChangesAsync(Target, 1, 20, Now, CancellationToken.None);
            var result = await sut.RecordChangesAsync(Target, 1, 40, Now.AddSeconds(1), CancellationToken.None);

            result.WasTriggered.Should().BeTrue();
            store.Dispatches.Should().ContainSingle()
                .Which.RecordCount.Should().Be(2);
            store.Dispatches[0].ByteCount.Should().Be(60);
        }

        [Test]
        public async Task TriggerNamedBulkAsync_AlwaysTriggersAndRecordsRunIdentity()
        {
            var store = new RecordingTriggerStore();
            var sut = new RecoveryTriggerCoordinator(store, new RecoveryTriggerPolicy(new()));

            var first = await sut.TriggerNamedBulkAsync(Target, "family-pro-12/run-42", Now, CancellationToken.None);
            var second = await sut.TriggerNamedBulkAsync(Target, "family-pro-12/run-42", Now, CancellationToken.None);

            first.WasTriggered.Should().BeTrue();
            second.WasTriggered.Should().BeTrue();
            store.Dispatches.Should().HaveCount(2);
            store.Dispatches.Should().OnlyContain(value => value.TriggerReason.Contains("family-pro-12/run-42"));
        }

        [Test]
        public async Task TriggerScheduledAsync_UsesThirtyMinuteCadence()
        {
            var store = new RecordingTriggerStore();
            var sut = new RecoveryTriggerCoordinator(store, new RecoveryTriggerPolicy(new()));

            var due = await sut.TriggerScheduledAsync(Target, Now, CancellationToken.None);
            var early = await sut.TriggerScheduledAsync(Target, Now.AddMinutes(29), CancellationToken.None);
            var next = await sut.TriggerScheduledAsync(Target, Now.AddMinutes(30), CancellationToken.None);

            due.WasTriggered.Should().BeTrue();
            early.WasTriggered.Should().BeFalse();
            next.WasTriggered.Should().BeTrue();
        }

        [Test]
        public async Task TriggerPostReconciliationAsync_RecordsSuccessfulRunIdentity()
        {
            var store = new RecordingTriggerStore();
            var sut = new RecoveryTriggerCoordinator(store, new RecoveryTriggerPolicy(new()));

            var result = await sut.TriggerPostReconciliationAsync(
                Target,
                "import-run-9",
                Now,
                CancellationToken.None);

            result.WasTriggered.Should().BeTrue();
            result.TriggerReason.Should().Be("post-reconciliation:import-run-9");
        }

        [Test]
        public async Task ExecuteAsync_WhenMandatoryBackupIsNotVerified_BlocksDestructiveStart()
        {
            var store = new RecordingTriggerStore();
            var coordinator = new RecoveryTriggerCoordinator(store, new RecoveryTriggerPolicy(new()));
            var completion = new StubCompletionSource(new VerifiedRecoverySetEvidence(
                Guid.NewGuid(),
                null,
                false,
                false,
                false,
                "capture.failed"));
            var gate = new MandatoryPreOperationBackupGate(coordinator, completion);
            var started = false;

            var act = () => gate.ExecuteAsync(
                Target,
                "ledger-reset",
                "reset-plan-7",
                Now,
                _ =>
                {
                    started = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<MandatoryBackupFailedException>();
            started.Should().BeFalse();
        }

        [Test]
        public async Task ExecuteAsync_WhenIndependentPublicationIsVerified_StartsDestructiveWork()
        {
            var store = new RecordingTriggerStore();
            var coordinator = new RecoveryTriggerCoordinator(store, new RecoveryTriggerPolicy(new()));
            var operationId = Guid.Empty;
            var completion = new CallbackCompletionSource(id =>
            {
                operationId = id;
                return new VerifiedRecoverySetEvidence(id, Guid.NewGuid(), true, true, true, null);
            });
            var gate = new MandatoryPreOperationBackupGate(coordinator, completion);
            var started = false;

            await gate.ExecuteAsync(
                Target,
                "replace",
                "replace-plan-3",
                Now,
                _ =>
                {
                    started = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            started.Should().BeTrue();
            operationId.Should().NotBeEmpty();
        }

        private static RecoveryTriggerTarget Target => new(
            "integration",
            "default",
            "legacy",
            "ledger:integration:default",
            "scheduler",
            "kms://home-ledger/recovery",
            new[] { "accounting-sql", "eventstore", "mongo", "rates-sql" });

        private sealed class RecordingTriggerStore : IRecoveryTriggerStateStore
        {
            private RecoveryTriggerState _state;

            public List<RecoveryTriggerDispatch> Dispatches { get; } = new();

            public Task<RecoveryTriggerState> GetAsync(
                RecoveryTriggerTarget target,
                CancellationToken cancellationToken) => Task.FromResult(_state);

            public Task<bool> TryCommitAsync(
                RecoveryTriggerCommit commit,
                CancellationToken cancellationToken)
            {
                if (!TokensEqual(_state?.ConcurrencyToken, commit.ExpectedConcurrencyToken))
                {
                    return Task.FromResult(false);
                }

                _state = commit.NextState with { ConcurrencyToken = BitConverter.GetBytes((_state?.Version ?? 0) + 1) };
                if (commit.Dispatch is not null)
                {
                    Dispatches.Add(commit.Dispatch);
                }

                return Task.FromResult(true);
            }

            private static bool TokensEqual(byte[] left, byte[] right) =>
                left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
        }

        private sealed class StubCompletionSource(VerifiedRecoverySetEvidence evidence)
            : IVerifiedRecoverySetCompletionSource
        {
            public Task<VerifiedRecoverySetEvidence> WaitForVerifiedPublicationAsync(
                Guid operationId,
                CancellationToken cancellationToken) => Task.FromResult(evidence with { OperationId = operationId });
        }

        private sealed class CallbackCompletionSource(Func<Guid, VerifiedRecoverySetEvidence> callback)
            : IVerifiedRecoverySetCompletionSource
        {
            public Task<VerifiedRecoverySetEvidence> WaitForVerifiedPublicationAsync(
                Guid operationId,
                CancellationToken cancellationToken) => Task.FromResult(callback(operationId));
        }
    }
}
