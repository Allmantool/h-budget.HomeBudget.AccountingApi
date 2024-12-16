using System;

namespace HomeBudget.Core.Options
{
    public record EventStoreDbOptions
    {
        public Uri Url { get; init; }
    }
}
