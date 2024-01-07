﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Categories;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Clients.Interfaces;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public class PaymentOperationsHistoryServiceTests
    {
        [Test]
        public async Task SyncHistoryAsync_WhenTryToAddOperationWithTheSameKey_ThenIgnoreTheDuplicateForSameAccount()
        {
            var paymentAccountId = Guid.Parse("db2d514d-f571-4876-936a-784f24fc3060");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = EventTypes.Add,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = MockCategoriesStore.Categories.First(c => c.CategoryType == CategoryTypes.Income).Key,
                        Amount = 12.10m,
                    }
                },
                new()
                {
                    EventType = EventTypes.Add,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = MockCategoriesStore.Categories.First(c => c.CategoryType == CategoryTypes.Income).Key,
                        Amount = 12.10m,
                    }
                }
            };

            var sut = BuildServiceUnderTest(events);
            var result = await sut.SyncHistoryAsync(paymentAccountId);

            result.Payload.Should().Be(12.10m);
        }

        [Test]
        public async Task SyncHistory_WhenUpdateSeveralTimes_ThenTheMostUpToDateDataShouldBeApplied()
        {
            var paymentAccountId = Guid.Parse("36ab7a9c-66ac-4b6e-8765-469572daa46b");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = EventTypes.Add,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = MockCategoriesStore.Categories.First(c => c.CategoryType == CategoryTypes.Income).Key,
                        Amount = 12.10m,
                        OperationDay = new DateOnly(2023, 12, 12)
                    }
                },
                new()
                {
                    EventType = EventTypes.Update,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = MockCategoriesStore.Categories.First(c => c.CategoryType == CategoryTypes.Income).Key,
                        Amount = 17.12m,
                        OperationDay = new DateOnly(2023, 12, 15)
                    }
                },
                new()
                {
                    EventType = EventTypes.Update,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = MockCategoriesStore.Categories.First(c => c.CategoryType == CategoryTypes.Income).Key,
                        Amount = 98.98m,
                        OperationDay = new DateOnly(2023, 12, 12)
                    }
                }
            };

            var sut = BuildServiceUnderTest(events);
            var result = await sut.SyncHistoryAsync(paymentAccountId);

            result.Payload.Should().Be(17.12m);
        }

        [Test]
        public async Task SyncHistory_WhenRemoveNotExisted_ThenSkipForCalculation()
        {
            var paymentAccountId = Guid.Parse("1f34a993-8279-4f61-b86b-658c0d3703d7");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = EventTypes.Remove,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId
                    }
                }
            };

            var sut = BuildServiceUnderTest(events);
            var result = await sut.SyncHistoryAsync(paymentAccountId);

            result.Payload.Should().Be(0);
        }

        [Test]
        public async Task SyncHistory_WhenRemoveExisted_ThenShouldBeRemoved()
        {
            var paymentAccountId = Guid.Parse("d6971a06-5ab1-48f1-a6f6-1cd0cd1df220");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = EventTypes.Add,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        Amount = 12.10m,
                    }
                },
                new()
                {
                    EventType = EventTypes.Remove,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId
                    }
                }
            };

            var sut = BuildServiceUnderTest(events);
            var result = await sut.SyncHistoryAsync(paymentAccountId);

            result.Payload.Should().Be(0);
        }

        private PaymentOperationsHistoryService BuildServiceUnderTest(IEnumerable<PaymentOperationEvent> events)
        {
            var eventDbClient = new Mock<IEventStoreDbClient<PaymentOperationEvent>>();

            eventDbClient
                .Setup(cl => cl.ReadAsync(It.IsAny<string>(), CancellationToken.None))
                .Returns(events.ToAsyncEnumerable());

            var mongoClient = new Mock<IPaymentsHistoryDocumentsClient>();

            return new PaymentOperationsHistoryService(eventDbClient.Object, mongoClient.Object);
        }
    }
}