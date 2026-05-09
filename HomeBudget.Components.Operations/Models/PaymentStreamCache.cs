using System.Collections.Generic;

using EventStore.Client;

namespace HomeBudget.Components.Operations.Models
{
    internal sealed class PaymentStreamCache
    {
        public HashSet<Uuid> EventIds { get; } = [];

        public bool IsInitialized { get; set; }

        public StreamRevision? LatestRevision { get; set; }

        public Position LatestPosition { get; set; } = Position.Start;
    }
}
