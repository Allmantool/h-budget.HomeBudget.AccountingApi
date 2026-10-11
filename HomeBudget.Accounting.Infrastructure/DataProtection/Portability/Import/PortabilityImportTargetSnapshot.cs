namespace HomeBudget.Accounting.Infrastructure.DataProtection.Portability.Import
{
    public sealed record PortabilityImportTargetSnapshot
    {
        public string EnvironmentId { get; init; }

        public string LedgerId { get; init; }

        public string GenerationId { get; init; }

        public long GenerationNumber { get; init; }

        public bool HasBusinessState { get; init; }

        public bool IsActive { get; init; }

        public long AvailableBytes { get; init; }
    }
}
