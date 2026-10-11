using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public interface IDataProtectionAdministrationService
    {
        Task<DataProtectionAdministrativePlan> CreatePlanAsync(
            DataProtectionAdministrativePlanRequest request,
            TimeSpan confirmationLifetime,
            CancellationToken cancellationToken);

        Task<DataProtectionOperationStartResult> StartAsync(
            DataProtectionAdministrativeStartRequest request,
            CancellationToken cancellationToken);

        Task<DataProtectionOperation> GetAsync(Guid operationId, CancellationToken cancellationToken);

        Task<bool> RequestCancellationAsync(
            Guid operationId,
            string actorId,
            CancellationToken cancellationToken);
    }
}
