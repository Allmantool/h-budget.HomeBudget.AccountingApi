using System;
using System.Collections.Generic;

using HomeBudget.Core.Constants;

namespace HomeBudget.Core
{
    public abstract class BaseEvent
    {
        public long SequenceNumber { get; set; }

        public Guid EnvelopId { get; set; }

        public DateTime OccurredOn { get; } = DateTime.UtcNow;

        public DateTime ProcessedAt { get; set; }

        public Dictionary<string, string> Metadata { get; } = new()
        {
            {
                EventMetadataKeys.Version, "0.0.2"
            }
        };
    }
}
