using System;
using System.Collections.Generic;

using HomeBudget.Core.Constants;

namespace HomeBudget.Core
{
    public abstract class BaseEvent
    {
        public long SequenceNumber { get; set; }

        public DateTime OccurredOn { get; } = DateTime.UtcNow;

        public Dictionary<string, string> Metadata { get; set; } = new()
        {
            {
                EventMetadataKeys.Version, "0.0.1"
            }
        };
    }
}
