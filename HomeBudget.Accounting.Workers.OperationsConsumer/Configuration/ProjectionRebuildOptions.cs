namespace HomeBudget.Accounting.Workers.OperationsConsumer.Configuration
{
    internal sealed class ProjectionRebuildOptions
    {
        public const string SectionName = "ProjectionRebuild";

        public bool Enabled { get; init; }
        public bool Execute { get; init; }
        public bool OldWritersStopped { get; init; }
        public string ExpectedTarget { get; init; }
        public string ConfirmTarget { get; init; }
        public string ScopeFile { get; init; }
        public string PlanOutputFile { get; init; } = "payment-history-projection-rebuild-plan.json";
        public int MaxScopes { get; init; } = 50000;
    }
}
