using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal interface IDataProtectionPlanStore
    {
        Task CreateAsync(DataProtectionPlanRecord plan, CancellationToken cancellationToken);

        Task<DataProtectionOperationStartResult> TryConsumeAndEnqueueAsync(
            DataProtectionPlanRecord expectedPlan,
            DataProtectionOperationRequest operation,
            CancellationToken cancellationToken);
    }
}
