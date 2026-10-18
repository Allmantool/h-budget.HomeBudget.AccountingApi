using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;

namespace HomeBudget.Components.Operations.Tests
{
    [TestFixture]
    internal sealed class LedgerGenerationStreamNameResolverTests
    {
        [Test]
        public void Resolve_WhenLegacyScopeIsUsed_PreservesHistoricalStreamNameExactly()
        {
            var paymentEvent = CreateEvent("default", "legacy");

            var streamName = LedgerGenerationStreamNameResolver.Resolve(paymentEvent, "account/2026-10");

            streamName.Should().Be("account/2026-10");
        }

        [Test]
        public void Resolve_WhenDistinctUnsafeScopesAreUsed_ProducesDistinctPathSafeNamespaces()
        {
            var slash = LedgerGenerationStreamNameResolver.Resolve(
                CreateEvent("family/a", "generation\\one"),
                "account/2026-10");
            var dash = LedgerGenerationStreamNameResolver.Resolve(
                CreateEvent("family-a", "generation-one"),
                "account/2026-10");

            slash.Should().NotBe(dash);
            slash.Should().NotContain("family/a");
            slash.Should().NotContain("generation\\one");
            slash.Should().StartWith("ledger~");
        }

        [Test]
        public void Resolve_WhenUtf8ScopesDiffer_UsesTheirExactBytesAsTheNamespaceIdentity()
        {
            var composed = LedgerGenerationStreamNameResolver.Resolve(
                CreateEvent("caf\u00e9", "2026"),
                "account/2026-10");
            var decomposed = LedgerGenerationStreamNameResolver.Resolve(
                CreateEvent("cafe\u0301", "2026"),
                "account/2026-10");

            composed.Should().NotBe(decomposed);
        }

        private static PaymentOperationEvent CreateEvent(string ledgerId, string generationId)
        {
            var paymentEvent = new PaymentOperationEvent();
            paymentEvent.Metadata[EventMetadataKeys.LedgerId] = ledgerId;
            paymentEvent.Metadata[EventMetadataKeys.LedgerGenerationId] = generationId;
            return paymentEvent;
        }
    }
}
