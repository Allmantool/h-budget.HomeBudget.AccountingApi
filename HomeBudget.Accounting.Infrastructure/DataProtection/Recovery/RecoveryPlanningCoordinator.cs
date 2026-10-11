using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Recovery
{
    public sealed class RecoveryPlanningCoordinator
    {
        private readonly IRecoveryPlanningEvidenceSource _evidenceSource;
        private readonly RecoveryPlanSelector _selector;

        public RecoveryPlanningCoordinator(
            IRecoveryPlanningEvidenceSource evidenceSource,
            RecoveryPlanSelector selector)
        {
            ArgumentNullException.ThrowIfNull(evidenceSource);
            ArgumentNullException.ThrowIfNull(selector);
            _evidenceSource = evidenceSource;
            _selector = selector;
        }

        public async Task<RecoveryPlan> PlanAsync(
            RecoveryPlanningRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = await _evidenceSource.LoadAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return _selector.Select(request, evidence);
        }
    }
}
