using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.MongoDb
{
    public sealed class MongoDbProtectionPolicy
    {
        public MongoDbProtectionPolicy(
            string expectedDeploymentId,
            IEnumerable<string> allowedServerVersions,
            IEnumerable<string> allowedProviderIds)
        {
            ExpectedDeploymentId = RequireValue(expectedDeploymentId, nameof(expectedDeploymentId));
            AllowedServerVersions = CreateAllowlist(allowedServerVersions, nameof(allowedServerVersions));
            AllowedProviderIds = CreateAllowlist(allowedProviderIds, nameof(allowedProviderIds));
        }

        public string ExpectedDeploymentId { get; }

        public IReadOnlyCollection<string> AllowedServerVersions { get; }

        public IReadOnlyCollection<string> AllowedProviderIds { get; }

        private static string[] CreateAllowlist(IEnumerable<string> values, string parameterName)
        {
            var allowlist = values?.Select(value => RequireValue(value, parameterName))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return allowlist is { Length: > 0 }
                ? allowlist
                : throw new ArgumentException("At least one allowlisted value is required.", parameterName);
        }

        private static string RequireValue(string value, string parameterName) =>
            !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException("A non-empty value is required.", parameterName);
    }
}
