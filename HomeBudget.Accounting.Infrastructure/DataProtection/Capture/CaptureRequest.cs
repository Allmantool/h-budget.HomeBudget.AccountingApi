using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Capture
{
    internal sealed class CaptureRequest
    {
        public DataProtectionOperation Operation { get; init; }

        public DataProtectionOperationFenceContext FenceContext { get; init; }

        public string TriggerReason { get; init; }

        public string KeyReference { get; init; }

        public DateTimeOffset StartedUtc { get; init; }

        public IReadOnlyCollection<string> RequiredComponents { get; init; }

        public bool PreferPendingDrain { get; init; } = true;
    }
}
