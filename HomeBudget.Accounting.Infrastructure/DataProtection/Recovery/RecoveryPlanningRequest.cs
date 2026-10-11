using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed record RecoveryPlanningRequest(
        RecoveryPlanningMode Mode,
        Guid? RecoverySetId,
        DateTimeOffset RequestedCutoffUtc,
        string EnvironmentId,
        string LedgerId,
        string SourceGenerationId,
        string ApplicationVersion,
        string SchemaVersion,
        RecoveryTargetIdentity Target,
        bool SuppressNotifications,
        bool SuppressExternalSideEffects);

    public sealed record RecoveryTargetIdentity(
        string TargetId,
        string EnvironmentId,
        string LedgerId,
        string GenerationId,
        string ActiveGenerationId,
        bool IsIsolated,
        bool IsEmpty);
}
