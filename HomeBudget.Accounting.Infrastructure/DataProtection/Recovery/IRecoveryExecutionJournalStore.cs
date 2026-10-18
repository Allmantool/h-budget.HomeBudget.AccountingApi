using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public interface IRecoveryExecutionJournalStore
    {
        Task<RecoveryExecutionJournal> LoadAsync(Guid operationId, CancellationToken cancellationToken);

        Task<RecoveryExecutionJournal> CreateAsync(
            RecoveryExecutionJournal journal,
            CancellationToken cancellationToken);

        Task<RecoveryExecutionJournal> RecordStageAsync(
            Guid operationId,
            long expectedRevision,
            RecoveryExecutionStage stage,
            RecoveryStageCompletionEvidence evidence,
            CancellationToken cancellationToken);

        Task<RecoveryExecutionJournal> MarkFailedAsync(
            Guid operationId,
            long expectedRevision,
            string failureCode,
            bool rollbackEligible,
            CancellationToken cancellationToken);

        Task<RecoveryExecutionJournal> MarkAwaitingReplayRebuildAsync(
            Guid operationId,
            long expectedRevision,
            CancellationToken cancellationToken);
    }
}
