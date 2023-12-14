using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Components.Operations.Services;

namespace HomeBudget.Components.Operations.Tests
{
    [TestFixture]
    public class PaymentOperationsHistoryServiceTests
    {
        private readonly PaymentOperationsHistoryService _sut = new();

        [Test]
        public void SyncHistory_WhenRemoveItem_BalanceShouldBeEqualToZero()
        {
            var result = _sut.SyncHistory();

            result.Payload.Should().Be(0);
        }
    }
}