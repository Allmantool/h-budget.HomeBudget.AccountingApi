using System;
using System.Collections.Generic;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public class PaymentOperationsHistoryServiceTests
    {
        private readonly PaymentOperationsHistoryService _sut = new();

        [Test]
        public void SyncHistory_WhenTryToAddOperationWithTheSameKey_ThenIgnoreTheDuplicateForSameAccount()
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
                        Amount = 12.10m,
                    }
                }
            };

            MockOperationEventsStore.Events.AddRange(events);

            var result = _sut.SyncHistory(paymentAccountId);

            // TODO: Ok for now, for mock< but should be re-think in future
            result.Payload.Should().Be(12.10m);
        }

        [Test]
        public void SyncHistory_WhenUpdateSeveralTimes_ThenTheMostUpToDateDataShouldBeApplied()
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
                        Amount = 98.98m,
                        OperationDay = new DateOnly(2023, 12, 12)
                    }
                }
            };

            MockOperationEventsStore.Events.AddRange(events);

            var result = _sut.SyncHistory(paymentAccountId);

            result.Payload.Should().Be(17.12m);
        }

        [Test]
        public void SyncHistory_WhenTryToUpdateNotExistedOperation_ThenSkipForCalculation()
        {
            var paymentAccountId = Guid.Parse("b3246d4e-7893-487e-8d88-adfab91a8c86");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = EventTypes.Update,
                    Payload = new PaymentOperation
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        Amount = 12.10m,
                    }
                }
            };

            MockOperationEventsStore.Events.AddRange(events);

            var result = _sut.SyncHistory(paymentAccountId);

            result.Payload.Should().Be(0);
        }

        [Test]
        public void SyncHistory_WhenRemoveNotExisted_ThenSkipForCalculation()
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

            MockOperationEventsStore.Events.AddRange(events);

            var result = _sut.SyncHistory(paymentAccountId);

            result.Payload.Should().Be(0);
        }

        [Test]
        public void SyncHistory_WhenRemoveExisted_ThenShouldBeRemoved()
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

            MockOperationEventsStore.Events.AddRange(events);

            var result = _sut.SyncHistory(paymentAccountId);

            result.Payload.Should().Be(0);
        }
    }
}