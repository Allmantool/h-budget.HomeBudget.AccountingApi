using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed partial class EventStoreDbProtectionProvider : IEventStoreDbProtectionProvider
    {
        private readonly EventStoreDbProtectionPolicy _policy;
        private readonly IEventStoreDbProtectionProbe _probe;
        private readonly TimeProvider _timeProvider;

        public EventStoreDbProtectionProvider(
            EventStoreDbProtectionPolicy policy,
            IEventStoreDbProtectionProbe probe,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(probe);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _policy = policy;
            _probe = probe;
            _timeProvider = timeProvider;
        }

        public Task<EventStoreDbCapturePlan> PlanCaptureAsync(
            EventStoreDbCaptureKind kind,
            Guid planId,
            EventStoreDbBackupSet previousBackup,
            CancellationToken cancellationToken)
        {
            return PlanCaptureCoreAsync(kind, planId, previousBackup, cancellationToken);
        }

        public Task<EventStoreDbRestorePlan> PlanRestoreAsync(
            Guid planId,
            EventStoreDbBackupSet backup,
            string targetId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(backup);
            return PlanRestoreCoreAsync(planId, backup, targetId, cancellationToken);
        }
    }
}
