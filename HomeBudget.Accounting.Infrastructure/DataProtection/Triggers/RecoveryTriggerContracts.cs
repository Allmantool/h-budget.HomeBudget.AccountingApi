using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Triggers
{
    internal enum RecoveryTriggerKind
    {
        Scheduled,
        OnDemand,
        NamedBulk,
        Threshold,
        PreOperation,
        PostReconciliation
    }

    internal sealed class RecoveryTriggerOptions
    {
        public const string SectionName = "RecoveryProtection";

        public TimeSpan ScheduledCadence { get; init; } = TimeSpan.FromMinutes(30);

        public TimeSpan MaximumCoalescingDelay { get; init; } = TimeSpan.FromMinutes(15);

        public long RecordThreshold { get; init; } = 1000;

        public long ByteThreshold { get; init; } = 64 * 1024 * 1024;

        public TimeSpan SchedulerPollInterval { get; init; } = TimeSpan.FromMinutes(1);
    }

    internal sealed record RecoveryTriggerTarget(
        string EnvironmentId,
        string LedgerId,
        string GenerationId,
        string ScopeKey,
        string ActorId,
        string KeyReference,
        IReadOnlyCollection<string> RequiredComponents);

    internal sealed record RecoveryTriggerState(
        RecoveryTriggerTarget Target,
        long PendingRecordCount,
        long PendingByteCount,
        DateTimeOffset? FirstPendingUtc,
        DateTimeOffset? LastChangeUtc,
        DateTimeOffset? LastScheduledUtc,
        DateTimeOffset? LastTriggeredUtc,
        string LastTriggerReason,
        long Version,
        byte[] ConcurrencyToken);

    internal sealed record RecoveryTriggerDispatch(
        Guid OperationId,
        RecoveryTriggerKind TriggerKind,
        string TriggerReason,
        string SourceIdentity,
        long RecordCount,
        long ByteCount,
        DateTimeOffset CreatedUtc,
        RecoveryTriggerTarget Target,
        string PlanFingerprint);

    internal sealed record RecoveryTriggerCommit(
        byte[] ExpectedConcurrencyToken,
        RecoveryTriggerState NextState,
        RecoveryTriggerDispatch Dispatch);

    internal sealed record RecoveryTriggerResult(
        bool WasTriggered,
        Guid? OperationId,
        string TriggerReason);

    internal interface IRecoveryTriggerStateStore
    {
        Task<RecoveryTriggerState> GetAsync(
            RecoveryTriggerTarget target,
            CancellationToken cancellationToken);

        Task<bool> TryCommitAsync(
            RecoveryTriggerCommit commit,
            CancellationToken cancellationToken);
    }

    internal sealed class RecoveryTriggerPolicy
    {
        private readonly RecoveryTriggerOptions _options;

        public RecoveryTriggerPolicy(RecoveryTriggerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.ScheduledCadence <= TimeSpan.Zero
                || options.MaximumCoalescingDelay <= TimeSpan.Zero
                || options.RecordThreshold <= 0
                || options.ByteThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            _options = options;
        }

        public RecoveryTriggerEvaluation Evaluate(
            RecoveryTriggerState state,
            RecoveryTriggerTarget target,
            RecoveryTriggerSignal signal,
            DateTimeOffset nowUtc)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(signal);
            var current = state ?? Empty(target);
            var records = checked(current.PendingRecordCount + signal.RecordCount);
            var bytes = checked(current.PendingByteCount + signal.ByteCount);
            var hasChanges = records > 0 || bytes > 0;
            var firstPending = current.FirstPendingUtc ?? (hasChanges ? nowUtc : null);
            var threshold = signal.Kind == RecoveryTriggerKind.Threshold
                && (records >= _options.RecordThreshold || bytes >= _options.ByteThreshold);
            var bounded = signal.Kind == RecoveryTriggerKind.Threshold
                && hasChanges
                && firstPending.HasValue
                && nowUtc >= firstPending.Value + _options.MaximumCoalescingDelay;
            var scheduled = signal.Kind == RecoveryTriggerKind.Scheduled
                && (!current.LastScheduledUtc.HasValue
                    || nowUtc >= current.LastScheduledUtc.Value + _options.ScheduledCadence);
            var forced = signal.Kind is RecoveryTriggerKind.OnDemand
                or RecoveryTriggerKind.NamedBulk
                or RecoveryTriggerKind.PreOperation
                or RecoveryTriggerKind.PostReconciliation;
            var shouldTrigger = threshold || bounded || scheduled || forced;
            var reason = Reason(signal, threshold, bounded, records, bytes);
            var next = current with
            {
                PendingRecordCount = shouldTrigger ? 0 : records,
                PendingByteCount = shouldTrigger ? 0 : bytes,
                FirstPendingUtc = shouldTrigger ? null : firstPending,
                LastChangeUtc = signal.Kind == RecoveryTriggerKind.Threshold ? nowUtc : current.LastChangeUtc,
                LastScheduledUtc = scheduled ? nowUtc : current.LastScheduledUtc,
                LastTriggeredUtc = shouldTrigger ? nowUtc : current.LastTriggeredUtc,
                LastTriggerReason = shouldTrigger ? reason : current.LastTriggerReason
            };
            var requiresCommit = shouldTrigger || signal.Kind == RecoveryTriggerKind.Threshold;
            return new RecoveryTriggerEvaluation(next, shouldTrigger, requiresCommit, reason, records, bytes);
        }

        private static RecoveryTriggerState Empty(RecoveryTriggerTarget target) => new(
            target, 0, 0, null, null, null, null, null, 0, null);

        private static string Reason(
            RecoveryTriggerSignal signal,
            bool threshold,
            bool bounded,
            long records,
            long bytes)
        {
            return signal.Kind switch
            {
                RecoveryTriggerKind.Scheduled => "scheduled:cadence",
                RecoveryTriggerKind.OnDemand => $"on-demand:{signal.SourceIdentity}",
                RecoveryTriggerKind.NamedBulk => $"named-bulk:{signal.SourceIdentity}",
                RecoveryTriggerKind.PreOperation => $"pre-operation:{signal.OperationKind}:{signal.SourceIdentity}",
                RecoveryTriggerKind.PostReconciliation => $"post-reconciliation:{signal.SourceIdentity}",
                _ when bounded => $"threshold:bounded-coalescing;records={records};bytes={bytes}",
                _ when threshold => $"threshold:records={records};bytes={bytes}",
                _ => "threshold:coalesced"
            };
        }
    }

    internal sealed record RecoveryTriggerSignal(
        RecoveryTriggerKind Kind,
        string SourceIdentity,
        string OperationKind,
        long RecordCount,
        long ByteCount);

    internal sealed record RecoveryTriggerEvaluation(
        RecoveryTriggerState NextState,
        bool ShouldTrigger,
        bool RequiresCommit,
        string Reason,
        long RecordCount,
        long ByteCount);

    internal sealed class RecoveryTriggerCoordinator
    {
        private const int MaximumConcurrencyAttempts = 8;
        private readonly IRecoveryTriggerStateStore _store;
        private readonly RecoveryTriggerPolicy _policy;

        public RecoveryTriggerCoordinator(IRecoveryTriggerStateStore store, RecoveryTriggerPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(policy);
            _store = store;
            _policy = policy;
        }

        public Task<RecoveryTriggerResult> RecordChangesAsync(
            RecoveryTriggerTarget target,
            long recordCount,
            long byteCount,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => TriggerAsync(
                target,
                new RecoveryTriggerSignal(RecoveryTriggerKind.Threshold, null, null, recordCount, byteCount),
                nowUtc,
                cancellationToken);

        public Task<RecoveryTriggerResult> TriggerNamedBulkAsync(
            RecoveryTriggerTarget target,
            string runIdentity,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => TriggerAsync(
                target,
                new RecoveryTriggerSignal(RecoveryTriggerKind.NamedBulk, runIdentity, null, 0, 0),
                nowUtc,
                cancellationToken);

        public Task<RecoveryTriggerResult> TriggerScheduledAsync(
            RecoveryTriggerTarget target,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => TriggerAsync(
                target,
                new RecoveryTriggerSignal(RecoveryTriggerKind.Scheduled, null, null, 0, 0),
                nowUtc,
                cancellationToken);

        public Task<RecoveryTriggerResult> TriggerOnDemandAsync(
            RecoveryTriggerTarget target,
            string reason,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => TriggerAsync(
                target,
                new RecoveryTriggerSignal(RecoveryTriggerKind.OnDemand, reason, null, 0, 0),
                nowUtc,
                cancellationToken);

        public Task<RecoveryTriggerResult> TriggerPostReconciliationAsync(
            RecoveryTriggerTarget target,
            string runIdentity,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => TriggerAsync(
                target,
                new RecoveryTriggerSignal(RecoveryTriggerKind.PostReconciliation, runIdentity, null, 0, 0),
                nowUtc,
                cancellationToken);

        internal Task<RecoveryTriggerResult> TriggerPreOperationAsync(
            RecoveryTriggerTarget target,
            string operationKind,
            string sourceIdentity,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => TriggerAsync(
                target,
                new RecoveryTriggerSignal(
                    RecoveryTriggerKind.PreOperation,
                    sourceIdentity,
                    operationKind,
                    0,
                    0),
                nowUtc,
                cancellationToken);

        private async Task<RecoveryTriggerResult> TriggerAsync(
            RecoveryTriggerTarget target,
            RecoveryTriggerSignal signal,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            Validate(target, signal, nowUtc);
            for (var attempt = 0; attempt < MaximumConcurrencyAttempts; attempt++)
            {
                var state = await _store.GetAsync(target, cancellationToken).ConfigureAwait(false);
                var evaluation = _policy.Evaluate(state, target, signal, nowUtc);
                if (!evaluation.RequiresCommit)
                {
                    return new RecoveryTriggerResult(false, null, evaluation.Reason);
                }

                var dispatch = evaluation.ShouldTrigger
                    ? CreateDispatch(target, signal, evaluation, nowUtc)
                    : null;
                var committed = await _store.TryCommitAsync(
                    new RecoveryTriggerCommit(state?.ConcurrencyToken, evaluation.NextState, dispatch),
                    cancellationToken).ConfigureAwait(false);
                if (committed)
                {
                    return new RecoveryTriggerResult(
                        dispatch is not null,
                        dispatch?.OperationId,
                        evaluation.Reason);
                }
            }

            throw new InvalidOperationException("Recovery trigger state changed too frequently to commit safely.");
        }

        private static RecoveryTriggerDispatch CreateDispatch(
            RecoveryTriggerTarget target,
            RecoveryTriggerSignal signal,
            RecoveryTriggerEvaluation evaluation,
            DateTimeOffset nowUtc)
        {
            var operationId = Guid.NewGuid();
            var fingerprintValue = string.Join(
                "\n",
                "home-ledger-recovery-trigger/v1",
                operationId,
                target.EnvironmentId,
                target.LedgerId,
                target.GenerationId,
                target.ScopeKey,
                signal.Kind,
                evaluation.Reason,
                nowUtc.ToString("O"));
            var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintValue))).ToLowerInvariant();
            return new RecoveryTriggerDispatch(
                operationId,
                signal.Kind,
                evaluation.Reason,
                signal.SourceIdentity,
                evaluation.RecordCount,
                evaluation.ByteCount,
                nowUtc,
                target,
                fingerprint);
        }

        private static void Validate(
            RecoveryTriggerTarget target,
            RecoveryTriggerSignal signal,
            DateTimeOffset nowUtc)
        {
            ValidateText(target.EnvironmentId, nameof(target.EnvironmentId), 128);
            ValidateText(target.LedgerId, nameof(target.LedgerId), 128);
            ValidateText(target.GenerationId, nameof(target.GenerationId), 128);
            ValidateText(target.ScopeKey, nameof(target.ScopeKey), 256);
            ValidateText(target.ActorId, nameof(target.ActorId), 256);
            ValidateText(target.KeyReference, nameof(target.KeyReference), 512);
            ArgumentNullException.ThrowIfNull(target.RequiredComponents);
            if (target.RequiredComponents.Count == 0)
            {
                throw new ArgumentException("At least one required recovery component is required.", nameof(target));
            }

            foreach (var component in target.RequiredComponents)
            {
                ValidateText(component, nameof(target.RequiredComponents), 128);
            }

            ArgumentOutOfRangeException.ThrowIfNegative(signal.RecordCount);
            ArgumentOutOfRangeException.ThrowIfNegative(signal.ByteCount);
            if (nowUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Recovery trigger time must be UTC.", nameof(nowUtc));
            }

            if (signal.Kind is RecoveryTriggerKind.NamedBulk
                or RecoveryTriggerKind.OnDemand
                or RecoveryTriggerKind.PreOperation
                or RecoveryTriggerKind.PostReconciliation)
            {
                ValidateText(signal.SourceIdentity, nameof(signal.SourceIdentity), 256);
            }

            if (signal.Kind == RecoveryTriggerKind.PreOperation)
            {
                ValidateText(signal.OperationKind, nameof(signal.OperationKind), 128);
            }
        }

        private static void ValidateText(string value, string parameterName, int maximumLength)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
            if (value.Length > maximumLength)
            {
                throw new ArgumentException(
                    $"Value cannot exceed {maximumLength} characters.",
                    parameterName);
            }
        }
    }

    internal sealed record VerifiedRecoverySetEvidence(
        Guid OperationId,
        Guid? RecoverySetId,
        bool Succeeded,
        bool Verified,
        bool IndependentPublication,
        string FailureClassification);

    internal interface IVerifiedRecoverySetCompletionSource
    {
        Task<VerifiedRecoverySetEvidence> WaitForVerifiedPublicationAsync(
            Guid operationId,
            CancellationToken cancellationToken);
    }

    internal sealed class MandatoryBackupFailedException(string message) : InvalidOperationException(message)
    {
    }

    internal sealed class MandatoryPreOperationBackupGate
    {
        private readonly RecoveryTriggerCoordinator _coordinator;
        private readonly IVerifiedRecoverySetCompletionSource _completionSource;

        public MandatoryPreOperationBackupGate(
            RecoveryTriggerCoordinator coordinator,
            IVerifiedRecoverySetCompletionSource completionSource)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            ArgumentNullException.ThrowIfNull(completionSource);
            _coordinator = coordinator;
            _completionSource = completionSource;
        }

        public async Task ExecuteAsync(
            RecoveryTriggerTarget target,
            string operationKind,
            string sourceIdentity,
            DateTimeOffset nowUtc,
            Func<CancellationToken, Task> startDestructiveAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(startDestructiveAsync);
            var trigger = await _coordinator.TriggerPreOperationAsync(
                target,
                operationKind,
                sourceIdentity,
                nowUtc,
                cancellationToken).ConfigureAwait(false);
            if (!trigger.WasTriggered || !trigger.OperationId.HasValue)
            {
                throw new MandatoryBackupFailedException("Mandatory pre-operation backup was not queued.");
            }

            var evidence = await _completionSource.WaitForVerifiedPublicationAsync(
                trigger.OperationId.Value,
                cancellationToken).ConfigureAwait(false);
            if (evidence.OperationId != trigger.OperationId.Value
                || !evidence.Succeeded
                || !evidence.Verified
                || !evidence.IndependentPublication
                || !evidence.RecoverySetId.HasValue
                || evidence.RecoverySetId == Guid.Empty)
            {
                throw new MandatoryBackupFailedException(
                    $"Mandatory pre-operation backup failed: {evidence.FailureClassification ?? "unverified"}.");
            }

            await startDestructiveAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
