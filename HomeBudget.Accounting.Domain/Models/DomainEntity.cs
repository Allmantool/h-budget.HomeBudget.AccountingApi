﻿using System;

namespace HomeBudget.Accounting.Domain.Models
{
    public abstract class DomainEntity
    {
        public Guid Key { get; set; }

        public long OperationUnixTime { get; private set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
