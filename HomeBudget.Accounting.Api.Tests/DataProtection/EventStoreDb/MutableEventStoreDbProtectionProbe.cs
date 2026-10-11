using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.EventStoreDb
{
    internal sealed class MutableEventStoreDbProtectionProbe : IEventStoreDbProtectionProbe
    {
        public MutableEventStoreDbProtectionProbe(
            EventStoreDbSourceProbe source,
            EventStoreDbRestoreTargetProbe target)
        {
            Source = source;
            Target = target;
        }

        public EventStoreDbSourceProbe Source { get; set; }

        public EventStoreDbRestoreTargetProbe Target { get; set; }

        public Task<EventStoreDbSourceProbe> ProbeSourceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Source);
        }

        public Task<EventStoreDbRestoreTargetProbe> ProbeRestoreTargetAsync(
            string targetId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Target);
        }
    }
}
