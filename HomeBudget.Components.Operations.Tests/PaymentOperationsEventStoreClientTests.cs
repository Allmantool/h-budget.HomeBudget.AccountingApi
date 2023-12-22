using System;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Tests
{
    [TestFixture]
    public class PaymentOperationsEventStoreClientTests
    {
        private PaymentOperationsEventStoreClient _sut;

        [Test]
        public async Task SendAsync_When_Then()
        {
            var settings = EventStoreClientSettings.Create("esdb://localhost:2113?tls=false");

            var client = new EventStoreClient(settings);

            _sut = new PaymentOperationsEventStoreClient(client);

            var payload = new PaymentOperationEvent
            {
                EventType = EventTypes.Add,
                Payload = new PaymentOperation
                {
                    Key = Guid.Parse("8e41d373-3ef6-4cce-8bfd-e529a74b3aa7"),
                    PaymentAccountId = Guid.Parse("3605a215-8100-4bb3-804a-6ae2b39b2e43"),
                    Amount = 120.1m,
                    CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                    Comment = "Comment",
                    ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                    OperationDay = new DateOnly(2023, 12, 22)
                }
            };

            var eventType = $"{payload.EventType}_{payload.Payload.PaymentAccountId}_{payload.Payload.Key}";

            var result = await _sut.SendAsync(payload, eventType);

            result.LogPosition.CommitPosition.Should().Be(1);
        }
    }
}
