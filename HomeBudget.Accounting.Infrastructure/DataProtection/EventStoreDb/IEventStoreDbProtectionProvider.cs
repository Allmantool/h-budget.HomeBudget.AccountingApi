using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public interface IEventStoreDbProtectionProvider
    {
        Task<EventStoreDbCapturePlan> PlanCaptureAsync(
            EventStoreDbCaptureKind kind,
            Guid planId,
            EventStoreDbBackupSet previousBackup,
            CancellationToken cancellationToken);

        Task<EventStoreDbRestorePlan> PlanRestoreAsync(
            Guid planId,
            EventStoreDbBackupSet backup,
            string targetId,
            CancellationToken cancellationToken);
    }
}
