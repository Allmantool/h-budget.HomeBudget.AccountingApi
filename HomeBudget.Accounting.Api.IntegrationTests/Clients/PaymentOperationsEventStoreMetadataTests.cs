using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
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
using HomeBudget.Core.Observability;
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
        public async Task SendBatchAsync_WhenProducerSpanIsActive_ThenSerializesAppendSpanContextIntoEventMetadata()
        {
            var stoppedActivities = new ConcurrentBag<Activity>();
            using var source = new ActivitySource(nameof(PaymentOperationsEventStoreMetadataTests));
            var telemetrySourceName = Telemetry.ActivitySource.Name;
            using var listener = new ActivityListener
            {
                ShouldListenTo = activitySource => activitySource.Name == source.Name
                    || activitySource.Name == telemetrySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stoppedActivities.Add
            };
            ActivitySource.AddActivityListener(listener);

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
            paymentEvent.Metadata[EventMetadataKeys.TraceParent] = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbbbbbbbbbbbbb-01";
            paymentEvent.Metadata[EventMetadataKeys.MessageId] = Guid.NewGuid().ToString("N");
            paymentEvent.Metadata[EventMetadataKeys.CausationId] = "bbbbbbbbbbbbbbbb";

            var eventTypeTitle = $"{paymentEvent.EventType}_{paymentEvent.Payload.Key}";
            var streamName = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(paymentEvent.Payload.PaymentAccountId);

            using (var upstream = source.StartActivity("kafka.consume", ActivityKind.Consumer))
            {
                upstream.Should().NotBeNull();
                upstream!.TraceStateString = "rojo=00f067aa0ba902b7";
                using var baggageScope = TraceContextPropagation.UseExtractedBaggage(
                    TraceContextPropagation.Extract(
                        TraceContextPropagation.BuildCarrier(
                            upstream.Id,
                            upstream.TraceStateString,
                            "correlation.id=corr-123")));
                await sut.SendBatchAsync([paymentEvent], streamName, eventTypeTitle);
            }

            ResolvedEvent? storedEvent = null;
            await foreach (var resolvedEvent in client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start))
            {
                storedEvent = resolvedEvent;
                break;
            }

            storedEvent.Should().NotBeNull();
            storedEvent!.Value.Event.Metadata.Length.Should().BeGreaterThan(0);

            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(storedEvent.Value.Event.Metadata.Span);
            var appendActivity = stoppedActivities.Single(activity => activity.OperationName == "eventstore.append");

            metadata.Should().NotBeNull();
            metadata![EventMetadataKeys.CorrelationId].Should().Be("corr-123");
            metadata[EventMetadataKeys.TraceParent].Should().Be(appendActivity.Id);
            metadata[EventMetadataKeys.TraceId].Should().Be(appendActivity.TraceId.ToString());
            metadata[EventMetadataKeys.TraceState].Should().Be("rojo=00f067aa0ba902b7");
            metadata[EventMetadataKeys.Baggage].Should().Be("correlation.id=corr-123");
            metadata[EventMetadataKeys.MessageId].Should().Be(paymentEvent.Metadata[EventMetadataKeys.MessageId]);
            metadata[EventMetadataKeys.CausationId].Should().Be(appendActivity.SpanId.ToString());
        }
    }
}
