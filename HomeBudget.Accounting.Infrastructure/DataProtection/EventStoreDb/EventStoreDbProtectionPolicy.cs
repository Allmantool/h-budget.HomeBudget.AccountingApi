using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.EventStoreDb
{
    public sealed class EventStoreDbProtectionPolicy
    {
        public EventStoreDbProtectionPolicy(
            string expectedVersion,
            EventStoreDbNodePathIdentity source,
            IEnumerable<EventStoreDbNodePathIdentity> allowedSources,
            IEnumerable<string> allowedArtifactRoots,
            IEnumerable<EventStoreDbNodePathIdentity> allowedRestoreTargets)
        {
            ExpectedVersion = RequireValue(expectedVersion, nameof(expectedVersion));
            Source = Normalize(source, nameof(source));
            AllowedSources = Normalize(allowedSources, nameof(allowedSources));
            AllowedArtifactRoots = NormalizeRoots(allowedArtifactRoots, nameof(allowedArtifactRoots));
            AllowedRestoreTargets = Normalize(allowedRestoreTargets, nameof(allowedRestoreTargets));

            if (!AllowedSources.Contains(Source))
            {
                throw new ArgumentException("The configured source identity is not allowlisted.", nameof(source));
            }

            if (AllowedArtifactRoots.Any(root => Overlaps(root, Source.DatabasePath) ||
                Overlaps(root, Source.IndexPath)))
            {
                throw new ArgumentException("Artifact roots must not overlap source data paths.", nameof(allowedArtifactRoots));
            }
        }

        public string ExpectedVersion { get; }

        public EventStoreDbNodePathIdentity Source { get; }

        public IReadOnlyCollection<EventStoreDbNodePathIdentity> AllowedSources { get; }

        public IReadOnlyCollection<string> AllowedArtifactRoots { get; }

        public IReadOnlyCollection<EventStoreDbNodePathIdentity> AllowedRestoreTargets { get; }

        internal static string NormalizePath(string value, string parameterName)
        {
            var suppliedPath = RequireValue(value, parameterName);
            if (!Path.IsPathFullyQualified(suppliedPath))
            {
                throw new ArgumentException("An absolute path is required.", parameterName);
            }

            var path = Path.GetFullPath(suppliedPath);
            if (PathComparer.Equals(path, Path.GetPathRoot(path)))
            {
                throw new ArgumentException("A filesystem root cannot be allowlisted.", parameterName);
            }

            return Path.TrimEndingDirectorySeparator(path);
        }

        private static bool Overlaps(string left, string right)
        {
            return PathComparer.Equals(left, right) ||
                left.StartsWith(right + Path.DirectorySeparatorChar, PathComparison) ||
                right.StartsWith(left + Path.DirectorySeparatorChar, PathComparison);
        }

        private static EventStoreDbNodePathIdentity[] Normalize(
            IEnumerable<EventStoreDbNodePathIdentity> identities,
            string parameterName)
        {
            var values = identities?.Select(identity => Normalize(identity, parameterName))
                .Distinct()
                .ToArray();
            return values is { Length: > 0 }
                ? values
                : throw new ArgumentException("At least one identity is required.", parameterName);
        }

        private static EventStoreDbNodePathIdentity Normalize(
            EventStoreDbNodePathIdentity identity,
            string parameterName)
        {
            ArgumentNullException.ThrowIfNull(identity, parameterName);
            return new EventStoreDbNodePathIdentity(
                RequireValue(identity.NodeId, parameterName),
                NormalizePath(identity.DatabasePath, parameterName),
                NormalizePath(identity.IndexPath, parameterName));
        }

        private static string[] NormalizeRoots(
            IEnumerable<string> roots,
            string parameterName)
        {
            var values = roots?.Select(root => NormalizePath(root, parameterName))
                .Distinct(PathComparer)
                .ToArray();
            return values is { Length: > 0 }
                ? values
                : throw new ArgumentException("At least one artifact root is required.", parameterName);
        }

        private static string RequireValue(string value, string parameterName)
        {
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        internal static StringComparer PathComparer => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        private static StringComparison PathComparison => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
