using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed record RecoveryExecutionRequest(
        Guid OperationId,
        RecoveryPlan Plan,
        RecoveryArtifactBinding Artifact,
        RecoveryExecutionTargetEvidence Target);

    public sealed record RecoveryArtifactBinding(
        Guid RecoverySetId,
        string PlanFingerprint,
        string ManifestVersion,
        string ManifestSha256,
        string ApplicationVersion,
        string SchemaVersion,
        bool IsChecksumVerified);

    public sealed record RecoveryExecutionTargetEvidence(
        string TargetId,
        string EnvironmentId,
        string LedgerId,
        string GenerationId,
        string ActiveGenerationId,
        bool IsPositivelyIdentified,
        bool IsIsolated,
        bool IsEmpty,
        string EventStoreTargetId,
        bool IsEventStoreStopped,
        RecoveryMessagingResourceIdentity TopicNamespace,
        RecoveryMessagingResourceIdentity ConsumerGroup,
        RecoveryMessagingResourceIdentity Subscription);

    public sealed record RecoveryMessagingResourceIdentity(
        string ResourceId,
        string LedgerId,
        string GenerationId,
        bool IsDedicated);
}
