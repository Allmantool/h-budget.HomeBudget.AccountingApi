using System;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.DataProtection.Retention;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Retention
{
    [TestFixture]
    public sealed class RecoveryRetentionPolicyTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

        [Test]
        public void Defaults_MatchRecoveryContract()
        {
            var options = new RecoveryRetentionOptions();

            options.RetainAllFor.Should().Be(TimeSpan.FromHours(48));
            options.RetainDailyFor.Should().Be(TimeSpan.FromDays(30));
            options.RetainMonthlyFor.Should().Be(12);
            options.PointInTimeWindow.Should().Be(TimeSpan.FromDays(30));
        }

        [Test]
        public void Plan_RetainsDependenciesNeededBySelectedRecoveryPoint()
        {
            var nodes = new[]
            {
                Node("baseline", RecoveryChainNodeKind.Baseline, Now.AddDays(-40)),
                Node("log-1", RecoveryChainNodeKind.TransactionLog, Now.AddDays(-2), "baseline"),
                Node("point", RecoveryChainNodeKind.RecoveryPoint, Now.AddHours(-1), "log-1")
            };

            var plan = new RecoveryRetentionPolicy(new()).Plan(nodes, Now);

            plan.RetainedNodeIds.Should().BeEquivalentTo("baseline", "log-1", "point");
            plan.PrunableNodeIds.Should().BeEmpty();
            plan.RetentionReasons["baseline"].Should().Contain("point");
        }

        [Test]
        public void Plan_RetainsContinuousHistoryAndItsBaselineForPitrWindow()
        {
            var nodes = new[]
            {
                Node("baseline", RecoveryChainNodeKind.Baseline, Now.AddDays(-45)),
                Node(
                    "change-1",
                    RecoveryChainNodeKind.ChangeSegment,
                    Now.AddDays(-31),
                    "baseline",
                    Now.AddDays(-31),
                    Now.AddDays(-20)),
                Node(
                    "change-2",
                    RecoveryChainNodeKind.ChangeSegment,
                    Now.AddDays(-20),
                    "change-1",
                    Now.AddDays(-20),
                    Now)
            };

            var plan = new RecoveryRetentionPolicy(new()).Plan(nodes, Now);

            plan.RetainedNodeIds.Should().BeEquivalentTo("baseline", "change-1", "change-2");
        }

        [Test]
        public void Plan_PrunesOnlyExpiredUnreachableChain()
        {
            var nodes = new[]
            {
                Node("old-baseline", RecoveryChainNodeKind.Baseline, Now.AddMonths(-15)),
                Node("old-point", RecoveryChainNodeKind.RecoveryPoint, Now.AddMonths(-14), "old-baseline"),
                Node("current-point", RecoveryChainNodeKind.RecoveryPoint, Now.AddHours(-1))
            };

            var plan = new RecoveryRetentionPolicy(new()).Plan(nodes, Now);

            plan.PrunableNodeIds.Should().BeEquivalentTo("old-baseline", "old-point");
            plan.RetainedNodeIds.Should().Contain("current-point");
        }

        [Test]
        public void Plan_WhenDependencyIsMissing_RejectsPruningPlan()
        {
            var act = () => new RecoveryRetentionPolicy(new()).Plan(
                new[] { Node("point", RecoveryChainNodeKind.RecoveryPoint, Now, "missing") },
                Now);

            act.Should().Throw<InvalidOperationException>().WithMessage("*missing*");
        }

        private static RecoveryChainNode Node(
            string id,
            RecoveryChainNodeKind kind,
            DateTimeOffset createdUtc,
            string dependency = null,
            DateTimeOffset? coverageStartUtc = null,
            DateTimeOffset? coverageEndUtc = null) => new(
                id,
                kind,
                createdUtc,
                coverageStartUtc,
                coverageEndUtc,
                dependency is null ? Array.Empty<string>() : new[] { dependency });
    }
}
