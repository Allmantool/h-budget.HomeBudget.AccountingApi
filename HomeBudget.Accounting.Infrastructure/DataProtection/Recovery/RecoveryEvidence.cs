using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed record RecoveryEvidenceInventory(
        IReadOnlyCollection<VerifiedRecoverySetEvidence> RecoverySets,
        IReadOnlyCollection<RecoveryComponentChainEvidence> ComponentChains);

    public sealed record VerifiedRecoverySetEvidence(
        Guid RecoverySetId,
        string EnvironmentId,
        string LedgerId,
        string SourceGenerationId,
        string ApplicationVersion,
        string SchemaVersion,
        DateTimeOffset CommonCutoffUtc,
        bool IsComplete,
        bool IsProtected,
        bool IsIntegrityVerified,
        bool IsIndependentCopy,
        bool IsImmutableOrOffline,
        IReadOnlyCollection<RecoveryBaselineComponentEvidence> Components);

    public sealed record RecoveryBaselineComponentEvidence(
        RecoveryComponent Component,
        string ArtifactId,
        DateTimeOffset CutoffUtc,
        long DurableSequence,
        string Watermark,
        RecoveryHistoryCoverage Coverage,
        bool IsChecksumVerified,
        bool IsVersionCompatible);

    public sealed record RecoveryComponentChainEvidence(
        RecoveryComponent Component,
        DateTimeOffset RecoverableFromUtc,
        DateTimeOffset RecoverableToUtc,
        DateTimeOffset ResolvedCutoffUtc,
        long ResolvedSequence,
        string ResolvedWatermark,
        string Precision,
        IReadOnlyCollection<RecoveryHistorySegmentEvidence> Segments);

    public sealed record RecoveryHistorySegmentEvidence(
        RecoveryComponent Component,
        string ArtifactId,
        DateTimeOffset FromExclusiveUtc,
        DateTimeOffset ToInclusiveUtc,
        long FromSequenceExclusive,
        long ToSequenceInclusive,
        string Watermark,
        RecoveryHistoryCoverage Coverage,
        bool IsChecksumVerified,
        bool IsVersionCompatible,
        IReadOnlyCollection<RecoveryMutationEvidence> Mutations);

    public sealed record RecoveryMutationEvidence(
        string MutationId,
        RecoveryComponent Component,
        RecoveryMutationKind Kind,
        DateTimeOffset DurableTimestampUtc,
        long DurableSequence,
        DateOnly? BusinessDate);
}
