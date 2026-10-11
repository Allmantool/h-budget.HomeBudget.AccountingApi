using System;

using FluentAssertions;

using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Accounting.Api.Tests.DataProtection
{
    [TestFixture]
    internal sealed class ProjectionRebuildPlanBindingTests
    {
        [Test]
        public void ComputeOperationFingerprint_BindsExactScopeInventory()
        {
            var first = ProjectionRebuildPlanBinding.ComputeOperationFingerprint(
                "disposable",
                "ledger-a",
                "generation-7",
                new string('a', 64));
            var second = ProjectionRebuildPlanBinding.ComputeOperationFingerprint(
                "disposable",
                "ledger-a",
                "generation-7",
                new string('b', 64));

            first.Should().MatchRegex("^[0-9a-f]{64}$");
            second.Should().NotBe(first);
        }

        [TestCase("")]
        [TestCase("ABC")]
        [TestCase("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
        public void ComputeOperationFingerprint_RejectsInvalidInventoryFingerprint(string fingerprint)
        {
            var act = () => ProjectionRebuildPlanBinding.ComputeOperationFingerprint(
                "disposable",
                "ledger-a",
                "generation-7",
                fingerprint);

            act.Should().Throw<ArgumentException>();
        }
    }
}
