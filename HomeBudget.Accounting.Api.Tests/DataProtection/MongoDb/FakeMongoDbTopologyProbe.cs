using System.Threading;
using System.Threading.Tasks;
using HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.MongoDb
{
    internal sealed class FakeMongoDbTopologyProbe : IMongoDbTopologyProbe
    {
        public FakeMongoDbTopologyProbe(MongoDbTopologyEvidence evidence)
        {
            Evidence = evidence;
        }

        public MongoDbTopologyEvidence Evidence { get; set; }

        public Task<MongoDbTopologyEvidence> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Evidence);
        }
    }
}
