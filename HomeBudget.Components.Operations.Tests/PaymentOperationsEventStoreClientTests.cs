using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using DotNet.Testcontainers.Containers;
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
                .WithName($"{nameof(PaymentOperationsEventStoreClientTests)}-container")
                .WithHostname("test-event-store-db-host")
                .WithAutoRemove(true)
                .WithCleanUp(true)
                .WithPortBinding(3113, 2113)
                .Build();
        }

        [Test]
        public async Task SendAsync_WithSeveralEventsUnderDifferentPaymentAccounts_ThenCommitExpectedEventsAtStore()
        {
            var paymentAccountIdA = Guid.Parse("3605a215-8100-4bb3-804a-6ae2b39b2e43");
            var paymentAccountIdB = Guid.Parse("91c3d1bc-ce45-415a-a97d-2a9d834c7e02");

            await using (_eventSourceDbContainer)
            {
                if (_eventSourceDbContainer.State != TestcontainersStates.Running)
                {
                    await _eventSourceDbContainer.StartAsync();
                }

                var dbConnectionString = _eventSourceDbContainer.GetConnectionString();

                var client = new EventStoreClient(EventStoreClientSettings.Create(dbConnectionString));

                _sut = new PaymentOperationsEventStoreClient(client);

                var paymentsEvents = new List<PaymentOperationEvent>
                {
                    new()
                    {
                        EventType = PaymentEventTypes.Added,
                        Payload = new PaymentOperation
                        {
                            Key = Guid.Parse("7683a5d4-ba29-4274-8e9a-50de5361d46c"),
                            PaymentAccountId = paymentAccountIdA,
                            Amount = 199.1m,
                            CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                            ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                            Comment = "Account #1",
                            OperationDay = new DateOnly(2023, 12, 22)
                        }
                    },
                    new()
                    {
                        EventType = PaymentEventTypes.Removed,
                        Payload = new PaymentOperation
                        {
                            Key = Guid.Parse("2c683dd6-3eea-40f3-918b-ab1b60ccebc4"),
                            PaymentAccountId = paymentAccountIdA,
                            Amount = 120.1m,
                            CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                            ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                            Comment = "Account #1",
                            OperationDay = new DateOnly(2023, 12, 22)
                        }
                    },
                    new()
                    {
                        EventType = PaymentEventTypes.Updated,
                        Payload = new PaymentOperation
                        {
                            Key = Guid.Parse("2c683dd6-3eea-40f3-918b-ab1b60ccebc4"),
                            PaymentAccountId = paymentAccountIdA,
                            Amount = 160.1m,
                            CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                            ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                            Comment = "Account #1",
                            OperationDay = new DateOnly(2023, 12, 24)
                        }
                    },
                    new()
                    {
                        EventType = PaymentEventTypes.Added,
                        Payload = new PaymentOperation
                        {
                            Key = Guid.Parse("a2603450-7c16-4ac7-955a-5e261ccc0b89"),
                            PaymentAccountId = paymentAccountIdB,
                            Amount = 89.1m,
                            CategoryId = Guid.Parse("5fa3c529-c8ed-49a7-bf5c-d8f404d6adb7"),
                            ContractorId = Guid.Parse("4913aea0-07d9-4c31-b7d5-20361347319e"),
                            Comment = "Account #2",
                            OperationDay = new DateOnly(2023, 12, 27)
                        }
                    },
                };

                foreach (var paymentEvent in paymentsEvents)
                {
                    await _sut.SendAsync(paymentEvent);
                }

                var readResult = await _sut.ReadAsync(paymentAccountIdA.ToString()).ToListAsync();

                readResult.Count.Should().Be(paymentsEvents.Count(p => p.Payload.PaymentAccountId.CompareTo(paymentAccountIdA) == 0));

                await _eventSourceDbContainer.StopAsync();
            }
        }
    }
}
