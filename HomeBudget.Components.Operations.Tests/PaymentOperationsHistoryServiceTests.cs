using System;

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
            var result = _sut.SyncHistory(Guid.Parse("aed5a7ff-cd0f-4c65-b5ab-a3d7b8f9ac07"));

            result.Payload.Should().Be(0);
        }
    }
}