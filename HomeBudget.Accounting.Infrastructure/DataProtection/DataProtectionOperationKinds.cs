using System.Collections.Generic;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    public static class DataProtectionOperationKinds
    {
        public const string Backup = "backup";
        public const string Export = "export";
        public const string Import = "import";
        public const string Replace = "replace";
        public const string Restore = "restore";
        public const string PointInTimeRestore = "pitr";
        public const string ProjectionRebuild = "projection-rebuild";
        public const string LedgerReset = "ledger-reset";
        public const string EnvironmentReset = "environment-reset";

        private static readonly string[] KnownKinds =
        [
            Backup,
            Export,
            Import,
            Replace,
            Restore,
            PointInTimeRestore,
            ProjectionRebuild,
            LedgerReset,
            EnvironmentReset
        ];

        private static readonly HashSet<string> Supported = new(KnownKinds, System.StringComparer.Ordinal);

        public static IReadOnlyCollection<string> All { get; } = System.Array.AsReadOnly(KnownKinds);

        public static bool IsSupported(string operationKind)
        {
            return !string.IsNullOrWhiteSpace(operationKind) && Supported.Contains(operationKind);
        }

        public static bool RequiresRecentAuthentication(string operationKind)
        {
            return operationKind is Replace or Restore or PointInTimeRestore or LedgerReset or EnvironmentReset;
        }
    }
}
