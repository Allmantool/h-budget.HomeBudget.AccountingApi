using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public interface IMongoDbContinuousBackupAdapter
    {
        Task<MongoDbProviderCapabilityEvidence> ProbeCapabilityAsync(CancellationToken cancellationToken);

        Task<MongoDbSnapshotEvidence> CreateSnapshotAsync(CancellationToken cancellationToken);

        Task<MongoDbRecoveryPointEvidence> ResolveRecoveryPointAsync(
            DateTimeOffset requestedCutoffUtc,
            CancellationToken cancellationToken);
    }
}
