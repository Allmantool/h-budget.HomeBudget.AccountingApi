using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public interface IRecoveryCompletionJournalStore : IRecoveryExecutionJournalStore
    {
        Task<RecoveryExecutionJournal> RecordCompletionStageAsync(
            Guid operationId,
            long expectedRevision,
            RecoveryCompletionStageEvidence evidence,
            CancellationToken cancellationToken);

        Task<RecoveryExecutionJournal> MarkReadyToActivateAsync(
            Guid operationId,
            long expectedRevision,
            CancellationToken cancellationToken);

        Task<RecoveryExecutionJournal> MarkActivatedAsync(
            Guid operationId,
            long expectedRevision,
            RecoveryActivationEvidence evidence,
            CancellationToken cancellationToken);
    }
}
