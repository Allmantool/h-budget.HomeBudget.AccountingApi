﻿using System;

namespace HomeBudget.Core.Options
{
    public record EventStoreDbOptions
    {
        public Uri Url { get; init; }
        public int TimeoutInSeconds { get; init; } = 30;
        public int RetryAttempts { get; init; } = 3;
    }
}