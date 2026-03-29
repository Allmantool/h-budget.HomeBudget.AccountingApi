using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Handlers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Workers
{
    [TestFixture]
    public class PaymentOperationsDeliveryHandlerTracingTests
    {
        [Test]
        public async Task HandleAsync_WhenWritingEventStoreBatch_ThenPersistsActiveProducerTraceContext()
        {
            using var activitySource = new ActivitySource(nameof(PaymentOperationsDeliveryHandlerTracingTests));
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == activitySource.Name || source.Name == "HomeBudget.Accounting",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };

            ActivitySource.AddActivityListener(listener);

            using var firstConsume = activitySource.StartActivity("first.consume", ActivityKind.Consumer);
            var firstEnvelope = ActivityEnvelope<PaymentOperationEvent>.Capture(CreatePaymentEvent());

            using var secondConsume = activitySource.StartActivity("second.consume", ActivityKind.Consumer);
            var secondEnvelope = ActivityEnvelope<PaymentOperationEvent>.Capture(CreatePaymentEvent());

            var writeClient = new Mock<IEventStoreDbWriteClient<PaymentOperationEvent>>();
            writeClient
                .Setup(client => client.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<PaymentOperationEvent>, string, string, CancellationToken>((events, _, _, _) =>
                {
                    Activity.Current.Should().NotBeNull();
                    Activity.Current!.ParentSpanId.Should().Be(firstConsume!.SpanId);
                    Activity.Current.Links.Select(link => link.Context.SpanId).Should().Contain(secondConsume!.SpanId);

                    foreach (var paymentEvent in events)
                    {
                        paymentEvent.Metadata[EventMetadataKeys.TraceParent].Should().Be(Activity.Current.Id);
                        paymentEvent.Metadata[EventMetadataKeys.TraceId].Should().Be(Activity.Current.TraceId.ToString());
                        paymentEvent.Metadata[EventMetadataKeys.CausationId].Should().Be(Activity.Current.SpanId.ToString());
                    }
                })
                .ReturnsAsync(Mock.Of<IWriteResult>());

            var sut = new PaymentOperationsDeliveryHandler(
                Mock.Of<ILogger<PaymentOperationsDeliveryHandler>>(),
                Options.Create(new EventStoreDbOptions()),
                writeClient.Object);

            await sut.HandleAsync(
                new[]
                {
                    firstEnvelope,
                    secondEnvelope
                },
                CancellationToken.None);

            writeClient.Verify(
                client => client.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static PaymentOperationEvent CreatePaymentEvent()
        {
            return new PaymentOperationEvent
            {
                EventType = PaymentEventTypes.Added,
                Payload = new FinancialTransaction
                {
                    Key = System.Guid.NewGuid(),
                    PaymentAccountId = System.Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Amount = 12.3m,
                    CategoryId = System.Guid.NewGuid(),
                    ContractorId = System.Guid.NewGuid(),
                    Comment = "trace-test",
                    OperationDay = new System.DateOnly(2025, 1, 2)
                }
            };
        }
    }
}
