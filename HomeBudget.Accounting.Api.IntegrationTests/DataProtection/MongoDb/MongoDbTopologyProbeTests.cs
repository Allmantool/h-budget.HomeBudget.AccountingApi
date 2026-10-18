using System;
using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb;
using MongoDB.Driver;
using NUnit.Framework;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection.MongoDb
{
    [TestFixture]
    public sealed class MongoDbTopologyProbeTests : BaseIntegrationTests
    {
        [Test]
        public async Task ProbeCapabilityAsync_DisposableStandaloneMongo70_ReportsPitrBlocked()
        {
            var client = new MongoClient(TestContainers.MongoDbContainer.GetConnectionString());
            var probe = new MongoDbDriverTopologyProbe(client);
            var topology = await probe.ProbeAsync(CancellationToken.None);
            var adapter = new UnexpectedContinuousBackupAdapter();
            var policy = new MongoDbProtectionPolicy(
                "unused-for-blocked-standalone",
                new[] { topology.ServerVersion },
                new[] { "unused-provider" });
            var provider = new MongoDbProtectionProvider(policy, probe, adapter);

            var capability = await provider.ProbeCapabilityAsync(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(topology.ServerVersion, Does.StartWith("7.0."));
                Assert.That(topology.Topology, Is.EqualTo(MongoDbDeploymentTopology.Standalone));
                Assert.That(capability.State, Is.EqualTo(MongoDbPitrCapabilityState.Blocked));
                Assert.That(capability.ReasonCode, Is.EqualTo("standalone-topology"));
                Assert.That(adapter.ProbeCalls, Is.Zero);
            });
        }

        private sealed class UnexpectedContinuousBackupAdapter : IMongoDbContinuousBackupAdapter
        {
            public int ProbeCalls { get; private set; }

            public Task<MongoDbProviderCapabilityEvidence> ProbeCapabilityAsync(CancellationToken cancellationToken)
            {
                ProbeCalls++;
                throw new InvalidOperationException("The backup adapter must not be called for a standalone deployment.");
            }

            public Task<MongoDbSnapshotEvidence> CreateSnapshotAsync(CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The backup adapter must not be called for a standalone deployment.");

            public Task<MongoDbRecoveryPointEvidence> ResolveRecoveryPointAsync(
                DateTimeOffset requestedCutoffUtc,
                CancellationToken cancellationToken) =>
                throw new InvalidOperationException("The backup adapter must not be called for a standalone deployment.");
        }
    }
}
