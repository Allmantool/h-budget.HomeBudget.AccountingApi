using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public interface IMongoDbTopologyProbe
    {
        Task<MongoDbTopologyEvidence> ProbeAsync(CancellationToken cancellationToken);
    }
}
