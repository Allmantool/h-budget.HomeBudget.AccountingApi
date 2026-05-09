using EventStore.Client;

namespace HomeBudget.Accounting.Infrastructure.Clients
{
    public sealed class IdempotentWriteResult : IWriteResult
    {
        public IdempotentWriteResult(StreamRevision nextExpectedStreamRevision, Position logPosition)
        {
            NextExpectedStreamRevision = nextExpectedStreamRevision;
            NextExpectedVersion = nextExpectedStreamRevision.ToInt64();
            LogPosition = logPosition;
        }

        public long NextExpectedVersion { get; }

        public Position LogPosition { get; }

        public StreamRevision NextExpectedStreamRevision { get; }
    }
}
