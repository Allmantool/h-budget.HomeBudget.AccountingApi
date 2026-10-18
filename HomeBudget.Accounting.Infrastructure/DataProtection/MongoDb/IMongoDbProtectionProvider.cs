using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public interface IMongoDbProtectionProvider
    {
        Task<MongoDbPitrCapability> ProbeCapabilityAsync(CancellationToken cancellationToken);

        Task<MongoDbSnapshotEvidence> AcquireSnapshotAsync(CancellationToken cancellationToken);

        Task<MongoDbPointInTimeRecoveryPlan> PlanPointInTimeRecoveryAsync(
            DateTimeOffset requestedCutoffUtc,
            CancellationToken cancellationToken);
    }
}
