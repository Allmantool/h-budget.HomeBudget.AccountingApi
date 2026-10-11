using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.SqlServer
{
    public sealed class SqlServerProtectionPolicy
    {
        public SqlServerProtectionPolicy(
            string databaseName,
            string backupRoot,
            string expectedServerName,
            string expectedProductVersion,
            string expectedEdition,
            string expectedRecoveryModel,
            IEnumerable<string> allowedDatabaseNames,
            IEnumerable<string> allowedBackupRoots,
            int commandTimeoutSeconds)
        {
            DatabaseName = RequireValue(databaseName, nameof(databaseName));
            BackupRoot = ValidateServerPath(backupRoot, nameof(backupRoot));
            ExpectedServerName = RequireValue(expectedServerName, nameof(expectedServerName));
            ExpectedProductVersion = RequireValue(expectedProductVersion, nameof(expectedProductVersion));
            ExpectedEdition = RequireValue(expectedEdition, nameof(expectedEdition));
            ExpectedRecoveryModel = RequireValue(expectedRecoveryModel, nameof(expectedRecoveryModel));
            AllowedDatabaseNames = CreateAllowlist(allowedDatabaseNames, nameof(allowedDatabaseNames));
            AllowedBackupRoots = CreateAllowlist(allowedBackupRoots, nameof(allowedBackupRoots));

            if (!AllowedDatabaseNames.Contains(DatabaseName, StringComparer.Ordinal))
            {
                throw new ArgumentException("The configured database is not allowlisted.", nameof(databaseName));
            }

            if (!AllowedBackupRoots.Contains(BackupRoot, StringComparer.Ordinal))
            {
                throw new ArgumentException("The configured backup root is not allowlisted.", nameof(backupRoot));
            }

            if (commandTimeoutSeconds is < 1 or > 86400)
            {
                throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
            }

            CommandTimeoutSeconds = commandTimeoutSeconds;
        }

        public string DatabaseName { get; }

        public string BackupRoot { get; }

        public string ExpectedServerName { get; }

        public string ExpectedProductVersion { get; }

        public string ExpectedEdition { get; }

        public string ExpectedRecoveryModel { get; }

        public IReadOnlyCollection<string> AllowedDatabaseNames { get; }

        public IReadOnlyCollection<string> AllowedBackupRoots { get; }

        public int CommandTimeoutSeconds { get; }

        internal static string ValidateServerPath(string value, string parameterName)
        {
            var path = RequireValue(value, parameterName);
            if (!path.StartsWith("/", StringComparison.Ordinal) ||
                path.Contains("\\", StringComparison.Ordinal) ||
                path.Contains(":", StringComparison.Ordinal) ||
                path.Split('/').Any(segment => segment == ".."))
            {
                throw new ArgumentException("SQL backup targets must be absolute allowlisted POSIX paths.", parameterName);
            }

            return path.TrimEnd('/');
        }

        private static IReadOnlyCollection<string> CreateAllowlist(
            IEnumerable<string> values,
            string parameterName)
        {
            var allowlist = values?.Select(value => RequireValue(value, parameterName))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return allowlist is { Length: > 0 }
                ? allowlist
                : throw new ArgumentException("An allowlist must contain at least one value.", parameterName);
        }

        private static string RequireValue(string value, string parameterName)
        {
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }
}
