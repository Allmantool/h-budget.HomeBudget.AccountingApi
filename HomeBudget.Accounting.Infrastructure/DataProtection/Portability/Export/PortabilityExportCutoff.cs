using System;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Export
{
    public sealed record PortabilityExportCutoff
    {
        public DateTimeOffset RequestedCutoffUtc { get; init; }

        public DateTimeOffset ActualCutoffUtc { get; init; }

        public long CommonCutoffSequence { get; init; }

        public long ProjectionWatermarkSequence { get; init; }

        public DateTimeOffset ProjectionWatermarkUtc { get; init; }

        public string SourceGenerationId { get; init; }

        public long SourceGenerationNumber { get; init; }
    }
}
