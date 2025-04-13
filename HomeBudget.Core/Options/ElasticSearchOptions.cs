using System;

namespace HomeBudget.Core.Options
{
    public record ElasticSearchOptions
    {
        public bool IsEnabled { get; init; }

        public Uri Uri { get; init; }
    }
}
