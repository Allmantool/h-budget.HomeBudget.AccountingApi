using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed class MongoDbDriverTopologyProbe : IMongoDbTopologyProbe
    {
        private readonly IMongoDatabase _adminDatabase;
        private readonly TimeProvider _timeProvider;

        public MongoDbDriverTopologyProbe(IMongoClient client, TimeProvider timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(client);
            _adminDatabase = client.GetDatabase("admin");
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<MongoDbTopologyEvidence> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buildInfo = await RunCommandAsync("buildInfo", cancellationToken).ConfigureAwait(false);
            var hello = await RunCommandAsync("hello", cancellationToken).ConfigureAwait(false);
            var version = ReadRequiredString(buildInfo, "version");

            if (hello.TryGetValue("msg", out var message) && message.IsString &&
                string.Equals(message.AsString, "isdbgrid", StringComparison.Ordinal))
            {
                return Evidence(version, MongoDbDeploymentTopology.ShardedCluster, "mongodb-sharded", null);
            }

            if (!hello.TryGetValue("setName", out var setNameValue) || !setNameValue.IsString)
            {
                return Evidence(version, MongoDbDeploymentTopology.Standalone, "mongodb-standalone", null);
            }

            var setName = setNameValue.AsString;
            var configuration = await RunCommandAsync("replSetGetConfig", cancellationToken).ConfigureAwait(false);
            var replicaSetId = ReadReplicaSetId(configuration);
            return Evidence(
                version,
                MongoDbDeploymentTopology.ReplicaSet,
                BuildDeploymentId(setName, replicaSetId),
                setName);
        }

        private async Task<BsonDocument> RunCommandAsync(string commandName, CancellationToken cancellationToken) =>
            await _adminDatabase.RunCommandAsync<BsonDocument>(
                new BsonDocument(commandName, 1),
                cancellationToken: cancellationToken).ConfigureAwait(false);

        private MongoDbTopologyEvidence Evidence(
            string version,
            MongoDbDeploymentTopology topology,
            string deploymentId,
            string replicaSetName) => new(
                version,
                topology,
                deploymentId,
                replicaSetName,
                _timeProvider.GetUtcNow());

        private static string ReadRequiredString(BsonDocument document, string fieldName)
        {
            if (document.TryGetValue(fieldName, out var value) && value.IsString &&
                !string.IsNullOrWhiteSpace(value.AsString))
            {
                return value.AsString;
            }

            throw new InvalidOperationException("MongoDB topology evidence is incomplete.");
        }

        private static string ReadReplicaSetId(BsonDocument response)
        {
            if (response.TryGetValue("config", out var configValue) && configValue.IsBsonDocument &&
                configValue.AsBsonDocument.TryGetValue("settings", out var settingsValue) &&
                settingsValue.IsBsonDocument &&
                settingsValue.AsBsonDocument.TryGetValue("replicaSetId", out var idValue) &&
                !idValue.IsBsonNull)
            {
                return idValue.ToJson();
            }

            throw new InvalidOperationException("MongoDB replica-set identity evidence is incomplete.");
        }

        private static string BuildDeploymentId(string replicaSetName, string replicaSetId)
        {
            var identityBytes = Encoding.UTF8.GetBytes($"mongodb-replica-set\n{replicaSetName}\n{replicaSetId}");
            return $"mongodb-rs-sha256:{Convert.ToHexString(SHA256.HashData(identityBytes)).ToLowerInvariant()}";
        }
    }
}
