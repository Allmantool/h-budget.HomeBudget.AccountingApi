using System;

namespace HomeBudget.Core.Options
{
    public record SeqOptions
    {
        public bool IsEnabled { get; init; }

        public Uri Uri { get; init; }
    }
}
