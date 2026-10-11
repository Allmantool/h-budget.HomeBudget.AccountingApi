using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed partial class MongoDbProtectionProvider : IMongoDbProtectionProvider
    {
        private readonly MongoDbProtectionPolicy _policy;
        private readonly IMongoDbTopologyProbe _topologyProbe;
        private readonly IMongoDbContinuousBackupAdapter _backupAdapter;

        public MongoDbProtectionProvider(
            MongoDbProtectionPolicy policy,
            IMongoDbTopologyProbe topologyProbe,
            IMongoDbContinuousBackupAdapter backupAdapter)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(topologyProbe);
            ArgumentNullException.ThrowIfNull(backupAdapter);
            _policy = policy;
            _topologyProbe = topologyProbe;
            _backupAdapter = backupAdapter;
        }

        public async Task<MongoDbPitrCapability> ProbeCapabilityAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var topology = await ProbeTopologyAsync(cancellationToken).ConfigureAwait(false);
            var topologyBlocker = GetTopologyBlocker(topology);
            if (topologyBlocker is not null)
            {
                return Blocked(topology, topologyBlocker);
            }

            MongoDbProviderCapabilityEvidence provider;
            try
            {
                provider = await _backupAdapter.ProbeCapabilityAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
            {
                throw Failure("provider-probe-failed", "The MongoDB backup provider probe failed.");
            }

            var providerBlocker = GetProviderBlocker(provider, topology);
            return providerBlocker is null
                ? new MongoDbPitrCapability(
                    MongoDbPitrCapabilityState.Ready,
                    null,
                    topology.ServerVersion,
                    topology.Topology,
                    topology.DeploymentId,
                    provider.ProviderId,
                    provider.RecoverableFromUtc,
                    provider.RecoverableToUtc,
                    provider.Gaps)
                : Blocked(topology, providerBlocker, provider?.ProviderId);
        }

        public async Task<MongoDbSnapshotEvidence> AcquireSnapshotAsync(CancellationToken cancellationToken)
        {
            var capability = await RequireReadyCapabilityAsync(cancellationToken).ConfigureAwait(false);
            MongoDbSnapshotEvidence snapshot;
            try
            {
                snapshot = await _backupAdapter.CreateSnapshotAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
            {
                throw Failure("snapshot-provider-failed", "The MongoDB snapshot provider failed.");
            }

            ValidateSnapshot(snapshot, capability);
            return snapshot;
        }

        public async Task<MongoDbPointInTimeRecoveryPlan> PlanPointInTimeRecoveryAsync(
            DateTimeOffset requestedCutoffUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsUtc(requestedCutoffUtc))
            {
                throw Failure("cutoff-not-utc", "The requested MongoDB recovery cutoff must be UTC.");
            }

            var capability = await RequireReadyCapabilityAsync(cancellationToken).ConfigureAwait(false);
            ValidateRequestedCutoff(requestedCutoffUtc, capability);
            MongoDbRecoveryPointEvidence evidence;
            try
            {
                evidence = await _backupAdapter.ResolveRecoveryPointAsync(
                    requestedCutoffUtc,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
            {
                throw Failure("recovery-provider-failed", "The MongoDB recovery provider failed.");
            }

            ValidateRecoveryPoint(evidence, capability, requestedCutoffUtc);
            return new MongoDbPointInTimeRecoveryPlan(
                evidence.ProviderId,
                evidence.SourceDeploymentId,
                evidence.SnapshotId,
                evidence.ProviderArtifactId,
                evidence.RequestedCutoffUtc,
                evidence.ActualCutoffUtc,
                evidence.SnapshotTimeUtc,
                evidence.MutationFromUtc,
                evidence.MutationToUtc,
                evidence.Precision);
        }

        private async Task<MongoDbPitrCapability> RequireReadyCapabilityAsync(CancellationToken cancellationToken)
        {
            var capability = await ProbeCapabilityAsync(cancellationToken).ConfigureAwait(false);
            if (capability.State != MongoDbPitrCapabilityState.Ready)
            {
                throw Failure(
                    capability.ReasonCode ?? "pitr-capability-blocked",
                    "MongoDB point-in-time recovery capability is blocked.");
            }

            return capability;
        }

        private async Task<MongoDbTopologyEvidence> ProbeTopologyAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _topologyProbe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
            {
                throw Failure("topology-probe-failed", "The MongoDB topology probe failed.");
            }
        }
    }
}
