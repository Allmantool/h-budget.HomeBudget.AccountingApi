using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    internal interface IPortabilityImportJournalStore
    {
        Task<PortabilityImportJournal> CreateOrReadAsync(
            PortabilityImportJournalRequest request,
            CancellationToken cancellationToken);

        Task<PortabilityImportJournal> GetAsync(
            Guid operationId,
            CancellationToken cancellationToken);

        Task<bool> TryTransitionOutcomeAsync(
            PortabilityImportOutcomeTransition transition,
            CancellationToken cancellationToken);

        Task<bool> TryTransitionRunAsync(
            Guid operationId,
            string expectedState,
            string nextState,
            DateTime updatedUtc,
            CancellationToken cancellationToken);

        Task<bool> RecordReconciliationAsync(
            PortabilityImportReconciliationRecord record,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
