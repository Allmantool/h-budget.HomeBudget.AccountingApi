using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed partial class RecoveryExecutionCoordinator
    {
        private static void ValidateRequest(RecoveryExecutionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request.Plan);
            ArgumentNullException.ThrowIfNull(request.Artifact);
            ArgumentNullException.ThrowIfNull(request.Target);
            ValidateArtifact(request);
            ValidateTarget(request);
            ValidateMessagingResource(request.Target.TopicNamespace, request.Target);
            ValidateMessagingResource(request.Target.ConsumerGroup, request.Target);
            ValidateMessagingResource(request.Target.Subscription, request.Target);
        }

        private static void ValidateArtifact(RecoveryExecutionRequest request)
        {
            if (!request.Artifact.IsChecksumVerified || !IsSha256(request.Artifact.ManifestSha256))
            {
                throw Invalid("checksum-unverified", "The recovery manifest checksum is not verified.");
            }

            if (request.Artifact.RecoverySetId != request.Plan.BaselineRecoverySetId ||
                !string.Equals(request.Artifact.PlanFingerprint, request.Plan.PlanFingerprint, StringComparison.Ordinal))
            {
                throw Invalid("manifest-binding-mismatch", "The recovery manifest is not bound to the selected plan.");
            }

            if (!string.Equals(request.Artifact.ManifestVersion, request.Plan.SchemaVersion, StringComparison.Ordinal) ||
                !string.Equals(request.Artifact.ApplicationVersion, request.Plan.ApplicationVersion, StringComparison.Ordinal) ||
                !string.Equals(request.Artifact.SchemaVersion, request.Plan.SchemaVersion, StringComparison.Ordinal))
            {
                throw Invalid("version-incompatible", "The recovery manifest is not version compatible with the plan.");
            }
        }

        private static void ValidateTarget(RecoveryExecutionRequest request)
        {
            var expected = request.Plan.Target;
            var target = request.Target;
            if (!string.Equals(target.TargetId, expected.TargetId, StringComparison.Ordinal) ||
                !string.Equals(target.EnvironmentId, expected.EnvironmentId, StringComparison.Ordinal) ||
                !string.Equals(target.LedgerId, expected.LedgerId, StringComparison.Ordinal) ||
                !string.Equals(target.GenerationId, expected.GenerationId, StringComparison.Ordinal) ||
                !string.Equals(target.ActiveGenerationId, expected.ActiveGenerationId, StringComparison.Ordinal))
            {
                throw Invalid("target-binding-mismatch", "The restore target does not match the immutable plan.");
            }

            if (!target.IsPositivelyIdentified || !target.IsIsolated || !target.IsEmpty ||
                !expected.IsIsolated || !expected.IsEmpty ||
                string.Equals(target.GenerationId, target.ActiveGenerationId, StringComparison.Ordinal))
            {
                throw Invalid("target-not-isolated", "The restore target is not a verified isolated empty generation.");
            }

            if (!target.IsEventStoreStopped)
            {
                throw Invalid("eventstore-target-running", "EventStore restore requires stopped-target proof.");
            }

            if (string.IsNullOrWhiteSpace(target.EventStoreTargetId))
            {
                throw Invalid("eventstore-target-unverified", "EventStore restore target identity is missing.");
            }

            if (request.Plan.ActivationAllowed || !request.Plan.SuppressNotifications ||
                !request.Plan.SuppressExternalSideEffects)
            {
                throw Invalid("restore-plan-unsafe", "The recovery plan does not preserve isolated replay safety.");
            }
        }

        private static void ValidateMessagingResource(
            RecoveryMessagingResourceIdentity resource,
            RecoveryExecutionTargetEvidence target)
        {
            if (resource is null || string.IsNullOrWhiteSpace(resource.ResourceId) || !resource.IsDedicated ||
                !string.Equals(resource.LedgerId, target.LedgerId, StringComparison.Ordinal) ||
                !string.Equals(resource.GenerationId, target.GenerationId, StringComparison.Ordinal))
            {
                throw Invalid(
                    "messaging-target-not-generation-scoped",
                    "Restore messaging resources are not bound to the target ledger generation.");
            }
        }

        private static void ValidateResumeBinding(
            RecoveryExecutionRequest request,
            RecoveryExecutionJournal journal)
        {
            if (journal.OperationId != request.OperationId || journal.Artifact != request.Artifact ||
                journal.Target != request.Target ||
                !string.Equals(
                    journal.ExpectedActiveGenerationId,
                    request.Target.ActiveGenerationId,
                    StringComparison.Ordinal))
            {
                throw Invalid("resume-binding-conflict", "Restore resume evidence does not match the durable journal.");
            }

            if (journal.CompletedStages is null || journal.Revision < journal.CompletedStages.Count ||
                !IsOrderedPrefix(journal.CompletedStages))
            {
                throw Invalid("restore-journal-invalid", "Restore journal checkpoints are invalid.");
            }

            if (journal.State == RecoveryExecutionState.Failed && !journal.RollbackEligible)
            {
                throw Invalid("restore-journal-invalid", "Failed restore is not marked rollback eligible.");
            }
        }

        private static void ValidateCompletionEvidence(
            RecoveryExecutionRequest request,
            RecoveryExecutionStage stage,
            RecoveryStageCompletionEvidence evidence)
        {
            if (evidence is null || evidence.Stage != stage ||
                evidence.RecoverySetId != request.Artifact.RecoverySetId ||
                !string.Equals(evidence.PlanFingerprint, request.Plan.PlanFingerprint, StringComparison.Ordinal) ||
                !string.Equals(evidence.ManifestSha256, request.Artifact.ManifestSha256, StringComparison.Ordinal) ||
                !string.Equals(evidence.TargetId, request.Target.TargetId, StringComparison.Ordinal) ||
                !string.Equals(evidence.TargetGenerationId, request.Target.GenerationId, StringComparison.Ordinal) ||
                !evidence.IsChecksumVerified || !evidence.IsVersionCompatible ||
                !evidence.IsTargetIdentityVerified)
            {
                throw Invalid("restore-evidence-mismatch", "Restore stage completion evidence does not match the plan.");
            }
        }

        private static bool IsSha256(string value)
        {
            if (value is null || value.Length != 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsOrderedPrefix(
            System.Collections.Generic.IReadOnlyCollection<RecoveryExecutionStage> completedStages)
        {
            RecoveryExecutionStage[] allStages =
            [
                .. OrderedStages,
                RecoveryExecutionStage.ReplayAcceptedPending,
                RecoveryExecutionStage.RebuildProjections,
                RecoveryExecutionStage.ReconcileApis
            ];
            var index = 0;
            foreach (var stage in completedStages)
            {
                if (index >= allStages.Length || allStages[index] != stage)
                {
                    return false;
                }

                index++;
            }

            return true;
        }
    }
}
