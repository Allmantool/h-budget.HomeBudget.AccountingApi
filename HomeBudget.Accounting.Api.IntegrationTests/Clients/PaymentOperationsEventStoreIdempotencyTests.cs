using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    [Order(IntegrationTestOrderIndex.PaymentOperationsEventStoreClientTests)]
    public class PaymentOperationsEventStoreIdempotencyTests : BaseIntegrationTests
    {
        [Test]
        public async Task SendAsync_WhenSameMessageAppendedTwice_ThenStoresOneEvent()
        {
            var paymentEvent = CreatePaymentEvent();
            paymentEvent.Metadata[EventMetadataKeys.MessageId] = "same-message";
            paymentEvent.Metadata[EventMetadataKeys.CommandId] = "same-command";
            var streamName = GetStreamName(paymentEvent);
            var eventType = GetEventType(paymentEvent);
            using var client = CreateEventStoreClient();
            using var sut = CreateWriteClient(client);

            await sut.SendAsync(paymentEvent, streamName, eventType);
            await sut.SendAsync(paymentEvent, streamName, eventType);

            var storedEvents = await ReadEventsAsync(client, streamName);
            storedEvents.Should().HaveCount(1);
        }

        [Test]
        public async Task SendAsync_WhenConcurrentEventsTargetSameAccountMonth_ThenStoresEveryUniqueEvent()
        {
            var paymentAccountId = Guid.NewGuid();
            using var client = CreateEventStoreClient();
            using var sut = CreateWriteClient(client);
            var paymentEvents = Enumerable.Range(0, 25)
                .Select(i => CreatePaymentEvent(paymentAccountId, Guid.NewGuid(), 10 + i))
                .ToList();
            var streamName = GetStreamName(paymentEvents[0]);

            await Task.WhenAll(paymentEvents.Select(paymentEvent =>
                sut.SendAsync(paymentEvent, streamName, GetEventType(paymentEvent))));

            var storedEvents = await ReadEventsAsync(client, streamName);
            storedEvents.Should().HaveCount(paymentEvents.Count);
        }

        [Test]
        public async Task SendAsync_WhenRetriedAfterFirstAppendSucceeded_ThenDoesNotDuplicate()
        {
            var paymentEvent = CreatePaymentEvent();
            paymentEvent.Metadata[EventMetadataKeys.MessageId] = "retry-message";
            var streamName = GetStreamName(paymentEvent);
            var eventType = GetEventType(paymentEvent);
            using var client = CreateEventStoreClient();
            using var sut = CreateWriteClient(client);

            await sut.SendAsync(paymentEvent, streamName, eventType);
            await sut.SendAsync(paymentEvent, streamName, eventType);

            var storedEvents = await ReadEventsAsync(client, streamName);
            storedEvents.Should().HaveCount(1);
        }

        [Test]
        public async Task SendBatchAsync_WhenKafkaRedeliversSameMessage_ThenDoesNotDuplicate()
        {
            var paymentEvent = CreatePaymentEvent();
            paymentEvent.Metadata[EventMetadataKeys.MessageId] = "kafka-redelivery-message";
            paymentEvent.Metadata[EventMetadataKeys.CausationId] = "kafka-offset-42";
            paymentEvent.Metadata[EventMetadataKeys.SourceSystem] = "kafka";
            var streamName = GetStreamName(paymentEvent);
            var eventType = GetEventType(paymentEvent);
            using var client = CreateEventStoreClient();
            using var sut = CreateWriteClient(client);

            await sut.SendBatchAsync([paymentEvent], streamName, eventType);
            await sut.SendBatchAsync([paymentEvent], streamName, eventType);

            var storedEvents = await ReadEventsAsync(client, streamName);
            storedEvents.Should().HaveCount(1);
        }

        [Test]
        public async Task SendAsync_WhenMigrationRerunsWithoutMessageId_ThenOperationIdPreventsDuplicate()
        {
            var paymentAccountId = Guid.NewGuid();
            var operationId = Guid.NewGuid();
            var firstRun = CreatePaymentEvent(paymentAccountId, operationId, 90);
            var secondRun = CreatePaymentEvent(paymentAccountId, operationId, 90);
            var streamName = GetStreamName(firstRun);
            var eventType = GetEventType(firstRun);
            using var client = CreateEventStoreClient();
            using var sut = CreateWriteClient(client);

            await sut.SendAsync(firstRun, streamName, eventType);
            await sut.SendAsync(secondRun, streamName, eventType);

            var storedEvents = await ReadEventsAsync(client, streamName);
            storedEvents.Should().HaveCount(1);
            storedEvents[0].Event.EventId.Should().Be(storedEvents.Select(e => e.Event.EventId).Distinct().Single());
        }

        private EventStoreClient CreateEventStoreClient()
        {
            return new EventStoreClient(EventStoreClientSettings.Create(TestContainers.EventSourceDbContainer.GetConnectionString()));
        }

        private static PaymentOperationsEventStoreWriteClient CreateWriteClient(EventStoreClient client)
        {
            return new PaymentOperationsEventStoreWriteClient(
                Mock.Of<ILogger<PaymentOperationsEventStoreWriteClient>>(),
                client,
                Options.Create(new EventStoreDbOptions
                {
                    RetryAttempts = 3,
                    TimeoutInSeconds = 10,
                    RequestRateLimiter = 100
                }));
        }

        private static PaymentOperationEvent CreatePaymentEvent(
            Guid? paymentAccountId = null,
            Guid? operationId = null,
            decimal amount = 45)
        {
            return new PaymentOperationEvent
            {
                EventType = PaymentEventTypes.Added,
                Payload = new FinancialTransaction
                {
                    Key = operationId ?? Guid.NewGuid(),
                    PaymentAccountId = paymentAccountId ?? Guid.NewGuid(),
                    Amount = amount,
                    CategoryId = Guid.NewGuid(),
                    ContractorId = Guid.NewGuid(),
                    Comment = "idempotency-check",
                    OperationDay = new DateOnly(2026, 5, 9)
                }
            };
        }

        private static string GetStreamName(PaymentOperationEvent paymentEvent)
        {
            return PaymentOperationNamesGenerator.GenerateForAccountMonthStream(
                paymentEvent.Payload.GetMonthPeriodPaymentAccountIdentifier());
        }

        private static string GetEventType(PaymentOperationEvent paymentEvent)
            => $"{paymentEvent.EventType}_{paymentEvent.Payload.Key}";

        private static async Task<List<ResolvedEvent>> ReadEventsAsync(
            EventStoreClient client,
            string streamName)
        {
            var events = new List<ResolvedEvent>();
            var readResult = client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start);
            if (await readResult.ReadState == ReadState.StreamNotFound)
            {
                return events;
            }

            await foreach (var resolvedEvent in readResult)
            {
                events.Add(resolvedEvent);
            }

            return events;
        }
    }
}
