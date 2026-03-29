using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Moq;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain;
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
    public class PaymentOperationsEventStoreMetadataTests : BaseIntegrationTests
    {
        [Test]
        public async Task SendAsync_WhenTraceMetadataProvided_ThenStoresTraceMetadataInsideEventStoreMetadata()
        {
            var paymentAccountId = Guid.NewGuid();
            var dbConnectionString = TestContainers.EventSourceDbContainer.GetConnectionString();

            using var client = new EventStoreClient(EventStoreClientSettings.Create(dbConnectionString));
            using var sut = new PaymentOperationsEventStoreWriteClient(
                Mock.Of<ILogger<PaymentOperationsEventStoreWriteClient>>(),
                client,
                Options.Create(new EventStoreDbOptions
                {
                    RetryAttempts = 3,
                    TimeoutInSeconds = 10
                }));

            var paymentEvent = new PaymentOperationEvent
            {
                EventType = PaymentEventTypes.Added,
                Payload = new FinancialTransaction
                {
                    Key = Guid.NewGuid(),
                    PaymentAccountId = paymentAccountId,
                    Amount = 55.5m,
                    CategoryId = Guid.NewGuid(),
                    ContractorId = Guid.NewGuid(),
                    Comment = "metadata-check",
                    OperationDay = new DateOnly(2024, 3, 2)
                }
            };

            paymentEvent.Metadata[EventMetadataKeys.CorrelationId] = "corr-123";
            paymentEvent.Metadata[EventMetadataKeys.TraceParent] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
            paymentEvent.Metadata[EventMetadataKeys.TraceState] = "rojo=00f067aa0ba902b7";
            paymentEvent.Metadata[EventMetadataKeys.Baggage] = "correlation.id=corr-123";
            paymentEvent.Metadata[EventMetadataKeys.MessageId] = Guid.NewGuid().ToString("N");
            paymentEvent.Metadata[EventMetadataKeys.CausationId] = "00f067aa0ba902b7";

            var eventTypeTitle = $"{paymentEvent.EventType}_{paymentEvent.Payload.Key}";
            var streamName = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(paymentEvent.Payload.PaymentAccountId);

            await sut.SendAsync(paymentEvent, streamName, eventTypeTitle);

            ResolvedEvent? storedEvent = null;
            await foreach (var resolvedEvent in client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start))
            {
                storedEvent = resolvedEvent;
                break;
            }

            storedEvent.Should().NotBeNull();
            storedEvent!.Value.Event.Metadata.Length.Should().BeGreaterThan(0);

            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(storedEvent.Value.Event.Metadata.Span);

            metadata.Should().NotBeNull();
            metadata![EventMetadataKeys.CorrelationId].Should().Be("corr-123");
            metadata[EventMetadataKeys.TraceParent].Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
            metadata[EventMetadataKeys.TraceState].Should().Be("rojo=00f067aa0ba902b7");
            metadata[EventMetadataKeys.Baggage].Should().Be("correlation.id=corr-123");
            metadata[EventMetadataKeys.MessageId].Should().Be(paymentEvent.Metadata[EventMetadataKeys.MessageId]);
            metadata[EventMetadataKeys.CausationId].Should().Be("00f067aa0ba902b7");
        }
    }
}
