using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Triggers
{
    internal interface IRecoveryTriggerTargetSource
    {
        Task<IReadOnlyCollection<RecoveryTriggerTarget>> GetTargetsAsync(
            CancellationToken cancellationToken);
    }
}
