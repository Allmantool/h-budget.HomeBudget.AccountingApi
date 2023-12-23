using System;
using System.Linq;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;
using NUnit.Framework;
using Testcontainers.EventStoreDb;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Tests
{
    [TestFixture]
    public class PaymentOperationsEventStoreClientTests
    {
        private EventStoreDbContainer _eventSourceDbContainer;
        private PaymentOperationsEventStoreClient _sut;

        [OneTimeSetUp]
        public void Setup()
        {
            _eventSourceDbContainer = new EventStoreDbBuilder()
                .WithImage("eventstore/eventstore:23.10.0-jammy")
                .WithName("EventSourcingDb")
                .WithAutoRemove(true)
                .WithHostname("test-host")
                .WithCleanUp(true)
                .WithPortBinding(2113, 2113)
                .Build();
        }

        [Test]
        public async Task SendAsync_WhenAddParticularEvent_ThenCommitExpectedAmountOfData()
        {
            await using (_eventSourceDbContainer)
            {
                await _eventSourceDbContainer.StartAsync();

                var dbConnectionString = _eventSourceDbContainer.GetConnectionString();

                var client = new EventStoreClient(EventStoreClientSettings.Create(dbConnectionString));

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

                var writeResult = await _sut.SendAsync(payload, eventType);

                var readResult = await _sut.ReadAsync<PaymentOperationEvent>().ToListAsync();

                writeResult.LogPosition.CommitPosition.Should().Be(10505);
                readResult.Count.Should().Be(1);

                await _eventSourceDbContainer.StopAsync();
            }
        }
    }
}
