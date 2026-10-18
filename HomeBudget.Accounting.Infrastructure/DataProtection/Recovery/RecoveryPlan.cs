using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed record RecoveryPlan(
        string PlanFingerprint,
        RecoveryPlanningMode Mode,
        Guid BaselineRecoverySetId,
        DateTimeOffset RequestedCutoffUtc,
        DateTimeOffset ActualCutoffUtc,
        string Precision,
        string MembershipBasis,
        string ApplicationVersion,
        string SchemaVersion,
        string SourceGenerationId,
        RecoveryTargetIdentity Target,
        bool SuppressNotifications,
        bool SuppressExternalSideEffects,
        bool ActivationAllowed,
        IReadOnlyCollection<RecoveryComponentWatermark> ComponentWatermarks,
        IReadOnlyCollection<RecoveryHistorySegmentEvidence> SelectedSegments,
        IReadOnlyCollection<RecoveryMutationEvidence> IncludedMutations);

    public sealed record RecoveryComponentWatermark(
        RecoveryComponent Component,
        DateTimeOffset CutoffUtc,
        long DurableSequence,
        string Watermark,
        string Precision,
        IReadOnlyCollection<string> ArtifactIds);
}
