using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed record RecoveryExecutionJournal(
        Guid OperationId,
        RecoveryArtifactBinding Artifact,
        RecoveryExecutionTargetEvidence Target,
        string ExpectedActiveGenerationId,
        RecoveryExecutionState State,
        long Revision,
        IReadOnlyCollection<RecoveryExecutionStage> CompletedStages,
        bool RollbackEligible,
        string FailureCode);

    public sealed record RecoveryExecutionResult(
        Guid OperationId,
        RecoveryExecutionState State,
        long Revision,
        IReadOnlyCollection<RecoveryExecutionStage> CompletedStages,
        bool RollbackEligible,
        bool ActivationAllowed);
}
