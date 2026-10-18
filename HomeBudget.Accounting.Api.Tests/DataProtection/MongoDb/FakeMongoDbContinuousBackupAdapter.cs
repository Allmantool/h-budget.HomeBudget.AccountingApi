using System;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.MongoDb
{
    internal sealed class FakeMongoDbContinuousBackupAdapter : IMongoDbContinuousBackupAdapter
    {
        public FakeMongoDbContinuousBackupAdapter(
            MongoDbProviderCapabilityEvidence capability,
            MongoDbSnapshotEvidence snapshot,
            MongoDbRecoveryPointEvidence recoveryPoint)
        {
            Capability = capability;
            Snapshot = snapshot;
            RecoveryPoint = recoveryPoint;
        }

        public MongoDbProviderCapabilityEvidence Capability { get; set; }

        public MongoDbSnapshotEvidence Snapshot { get; set; }

        public MongoDbRecoveryPointEvidence RecoveryPoint { get; set; }

        public int CapabilityProbeCount { get; private set; }

        public Exception ProbeException { get; set; }

        public Task<MongoDbProviderCapabilityEvidence> ProbeCapabilityAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapabilityProbeCount++;
            return ProbeException is null
                ? Task.FromResult(Capability)
                : Task.FromException<MongoDbProviderCapabilityEvidence>(ProbeException);
        }

        public Task<MongoDbSnapshotEvidence> CreateSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }

        public Task<MongoDbRecoveryPointEvidence> ResolveRecoveryPointAsync(
            DateTimeOffset requestedCutoffUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(RecoveryPoint);
        }
    }
}
