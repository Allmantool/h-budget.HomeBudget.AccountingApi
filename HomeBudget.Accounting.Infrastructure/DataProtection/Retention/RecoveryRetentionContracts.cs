using System;
using System.Collections.Generic;
using System.Linq;

namespace HomeBudget.Accounting.Infrastructure.DataProtection.Retention
{
    internal enum RecoveryChainNodeKind
    {
        RecoveryPoint,
        Baseline,
        Differential,
        TransactionLog,
        ChangeSegment
    }

    internal sealed record RecoveryChainNode(
        string NodeId,
        RecoveryChainNodeKind Kind,
        DateTimeOffset CreatedUtc,
        DateTimeOffset? CoverageStartUtc,
        DateTimeOffset? CoverageEndUtc,
        IReadOnlyCollection<string> Dependencies,
        bool IsProtected = true);

    internal sealed class RecoveryRetentionOptions
    {
        public TimeSpan RetainAllFor { get; init; } = TimeSpan.FromHours(48);

        public TimeSpan RetainDailyFor { get; init; } = TimeSpan.FromDays(30);

        public int RetainMonthlyFor { get; init; } = 12;

        public TimeSpan PointInTimeWindow { get; init; } = TimeSpan.FromDays(30);
    }

    internal sealed record RecoveryRetentionPlan(
        IReadOnlyCollection<string> RetainedNodeIds,
        IReadOnlyCollection<string> PrunableNodeIds,
        IReadOnlyDictionary<string, string> RetentionReasons);

    internal sealed class RecoveryRetentionPolicy
    {
        private readonly RecoveryRetentionOptions _options;

        public RecoveryRetentionPolicy(RecoveryRetentionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.RetainAllFor <= TimeSpan.Zero
                || options.RetainDailyFor < options.RetainAllFor
                || options.RetainMonthlyFor <= 0
                || options.PointInTimeWindow <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            _options = options;
        }

        public RecoveryRetentionPlan Plan(
            IReadOnlyCollection<RecoveryChainNode> nodes,
            DateTimeOffset nowUtc)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            EnsureUtc(nowUtc, nameof(nowUtc));
            var byId = nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
            if (byId.Count != nodes.Count)
            {
                throw new InvalidOperationException("Recovery chain contains duplicate node identities.");
            }

            ValidateGraph(byId);
            var retained = new HashSet<string>(StringComparer.Ordinal);
            var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var seed in SelectSeeds(nodes, nowUtc))
            {
                RetainClosure(seed.NodeId, seed.NodeId, byId, retained, reasons);
            }

            var prunable = byId.Keys.Where(id => !retained.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            return new RecoveryRetentionPlan(
                retained.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                prunable,
                reasons);
        }

        private IEnumerable<RecoveryChainNode> SelectSeeds(
            IReadOnlyCollection<RecoveryChainNode> nodes,
            DateTimeOffset nowUtc)
        {
            var points = nodes
                .Where(node => node.Kind == RecoveryChainNodeKind.RecoveryPoint && node.IsProtected)
                .Where(node => node.CreatedUtc <= nowUtc)
                .ToArray();
            var retainAllSince = nowUtc - _options.RetainAllFor;
            foreach (var point in points.Where(node => node.CreatedUtc >= retainAllSince))
            {
                yield return point;
            }

            var dailySince = nowUtc - _options.RetainDailyFor;
            foreach (var point in points
                .Where(node => node.CreatedUtc >= dailySince && node.CreatedUtc < retainAllSince)
                .GroupBy(node => node.CreatedUtc.UtcDateTime.Date)
                .Select(group => group.MaxBy(node => node.CreatedUtc)))
            {
                yield return point;
            }

            var monthlySince = nowUtc.AddMonths(-_options.RetainMonthlyFor);
            foreach (var point in points
                .Where(node => node.CreatedUtc >= monthlySince && node.CreatedUtc < dailySince)
                .GroupBy(node => (node.CreatedUtc.Year, node.CreatedUtc.Month))
                .Select(group => group.MaxBy(node => node.CreatedUtc)))
            {
                yield return point;
            }

            var pitrStart = nowUtc - _options.PointInTimeWindow;
            foreach (var segment in nodes.Where(node =>
                         node.Kind is RecoveryChainNodeKind.TransactionLog or RecoveryChainNodeKind.ChangeSegment
                         && node.CoverageStartUtc.HasValue
                         && node.CoverageEndUtc.HasValue
                         && node.CoverageStartUtc.Value <= nowUtc
                         && node.CoverageEndUtc.Value >= pitrStart))
            {
                yield return segment;
            }
        }

        private static void ValidateGraph(IReadOnlyDictionary<string, RecoveryChainNode> nodes)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes.Values)
            {
                Visit(node, nodes, visiting, visited);
            }
        }

        private static void Visit(
            RecoveryChainNode node,
            IReadOnlyDictionary<string, RecoveryChainNode> nodes,
            ISet<string> visiting,
            ISet<string> visited)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(node.NodeId);
            EnsureUtc(node.CreatedUtc, nameof(node.CreatedUtc));
            if (node.CoverageStartUtc.HasValue != node.CoverageEndUtc.HasValue)
            {
                throw new InvalidOperationException(
                    $"Recovery chain node '{node.NodeId}' has an incomplete coverage interval.");
            }

            if (node.CoverageStartUtc.HasValue)
            {
                EnsureUtc(node.CoverageStartUtc.Value, nameof(node.CoverageStartUtc));
                EnsureUtc(node.CoverageEndUtc.Value, nameof(node.CoverageEndUtc));
                if (node.CoverageStartUtc.Value > node.CoverageEndUtc.Value)
                {
                    throw new InvalidOperationException(
                        $"Recovery chain node '{node.NodeId}' has an invalid coverage interval.");
                }
            }

            if (visited.Contains(node.NodeId))
            {
                return;
            }

            if (!visiting.Add(node.NodeId))
            {
                throw new InvalidOperationException($"Recovery chain contains a cycle at '{node.NodeId}'.");
            }

            foreach (var dependencyId in node.Dependencies ?? Array.Empty<string>())
            {
                if (!nodes.TryGetValue(dependencyId, out var dependency))
                {
                    throw new InvalidOperationException(
                        $"Recovery chain dependency '{dependencyId}' required by '{node.NodeId}' is missing.");
                }

                Visit(dependency, nodes, visiting, visited);
            }

            visiting.Remove(node.NodeId);
            visited.Add(node.NodeId);
        }

        private static void RetainClosure(
            string nodeId,
            string rootId,
            IReadOnlyDictionary<string, RecoveryChainNode> nodes,
            ISet<string> retained,
            IDictionary<string, string> reasons)
        {
            if (!retained.Add(nodeId))
            {
                return;
            }

            reasons[nodeId] = nodeId == rootId ? $"policy-seed:{rootId}" : $"dependency-of:{rootId}";
            foreach (var dependencyId in nodes[nodeId].Dependencies ?? Array.Empty<string>())
            {
                RetainClosure(dependencyId, rootId, nodes, retained, reasons);
            }
        }

        private static void EnsureUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Recovery retention timestamps must be UTC.", parameterName);
            }
        }
    }
}
