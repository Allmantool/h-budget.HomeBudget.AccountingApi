using System.Collections.Generic;
using System.Collections.Concurrent;
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

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;
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
        public async Task HandleAsync_WhenMaintenanceIsClosed_DoesNotAppendToEventStore()
        {
            var writeClient = new Mock<IEventStoreDbWriteClient<PaymentOperationEvent>>();
            var fence = new Mock<IMaintenanceAdmissionFence>();
            fence.Setup(service => service.AcquireAdmissionPermitAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MaintenanceAdmissionClosedException());
            var sut = new PaymentOperationsDeliveryHandler(
                Mock.Of<ILogger<PaymentOperationsDeliveryHandler>>(),
                Options.Create(new EventStoreDbOptions()),
                writeClient.Object,
                fence.Object,
                CreateGenerationResolver());

            var act = () => sut.HandleAsync(
                new[] { ActivityEnvelope<PaymentOperationEvent>.Capture(CreatePaymentEvent()) },
                CancellationToken.None);

            await act.Should().ThrowAsync<MaintenanceAdmissionClosedException>();
            writeClient.VerifyNoOtherCalls();
        }

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

            var originalActivity = Activity.Current;

            try
            {
                Activity.Current = null;

                using var firstConsume = activitySource.StartActivity("first.consume", ActivityKind.Consumer);
                firstConsume.Should().NotBeNull();
                var firstEnvelope = ActivityEnvelope<PaymentOperationEvent>.Capture(CreatePaymentEvent());
                firstConsume!.Stop();

                Activity.Current = null;

                using var secondConsume = activitySource.StartActivity("second.consume", ActivityKind.Consumer);
                secondConsume.Should().NotBeNull();
                var secondEnvelope = ActivityEnvelope<PaymentOperationEvent>.Capture(CreatePaymentEvent());
                secondConsume!.Stop();

                Activity.Current = null;

                var writeClient = new Mock<IEventStoreDbWriteClient<PaymentOperationEvent>>();
                writeClient
                    .Setup(client => client.SendBatchAsync(
                        It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<IEnumerable<PaymentOperationEvent>, string, string, CancellationToken>((events, _, _, _) =>
                    {
                        var writeActivity = Activity.Current;
                        writeActivity.Should().NotBeNull();
                        writeActivity!.ParentSpanId.Should().Be(firstConsume.SpanId);
                        writeActivity.Links.Select(link => link.Context.SpanId).Should().Contain(secondConsume.SpanId);

                        foreach (var paymentEvent in events)
                        {
                            paymentEvent.Metadata[EventMetadataKeys.TraceParent].Should().Be(writeActivity.Id);
                            paymentEvent.Metadata[EventMetadataKeys.TraceId].Should().Be(writeActivity.TraceId.ToString());
                            paymentEvent.Metadata[EventMetadataKeys.CausationId].Should().Be(writeActivity.SpanId.ToString());
                        }
                    })
                    .ReturnsAsync(Mock.Of<IWriteResult>());

                var sut = new PaymentOperationsDeliveryHandler(
                    Mock.Of<ILogger<PaymentOperationsDeliveryHandler>>(),
                    Options.Create(new EventStoreDbOptions()),
                    writeClient.Object,
                    CreateAdmissionFence(),
                    CreateGenerationResolver());

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
            finally
            {
                Activity.Current = originalActivity;
            }
        }

        [Test]
        public async Task HandleAsync_WhenSameAccountMonthExistsInTwoLedgerGenerations_UsesDistinctStreamsAndPreservesLegacyName()
        {
            var legacy = CreatePaymentEvent();
            legacy.Metadata["ledger-id"] = "default";
            legacy.Metadata["ledger-generation-id"] = "legacy";
            var replacement = CreatePaymentEvent();
            replacement.Metadata["ledger-id"] = "default";
            replacement.Metadata["ledger-generation-id"] = "replacement-1";
            var streams = new ConcurrentBag<string>();
            var writeClient = new Mock<IEventStoreDbWriteClient<PaymentOperationEvent>>();
            writeClient
                .Setup(client => client.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<PaymentOperationEvent>, string, string, CancellationToken>((_, stream, _, _) =>
                    streams.Add(stream))
                .ReturnsAsync(Mock.Of<IWriteResult>());
            var sut = new PaymentOperationsDeliveryHandler(
                Mock.Of<ILogger<PaymentOperationsDeliveryHandler>>(),
                Options.Create(new EventStoreDbOptions()),
                writeClient.Object,
                CreateAdmissionFence(),
                CreateGenerationResolver());

            await sut.HandleAsync(
                new[]
                {
                    ActivityEnvelope<PaymentOperationEvent>.Capture(legacy),
                    ActivityEnvelope<PaymentOperationEvent>.Capture(replacement)
                },
                CancellationToken.None);

            var exactLegacyStream = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(
                legacy.Payload.GetMonthPeriodPaymentAccountIdentifier());
            streams.Should().Contain(exactLegacyStream, "generation zero stream identities cannot be renamed");
            streams.Should().HaveCount(2, "event identity and idempotency are isolated by ledger generation");
            streams.Should().OnlyHaveUniqueItems();
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

        private static IMaintenanceAdmissionFence CreateAdmissionFence()
        {
            var fence = new Mock<IMaintenanceAdmissionFence>();
            fence.Setup(service => service.AcquireAdmissionPermitAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<IMaintenanceAdmissionPermit>());
            return fence.Object;
        }

        private static IActiveLedgerGenerationResolver CreateGenerationResolver()
        {
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver.Setup(service => service.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(LedgerGenerationScope.Legacy);
            return resolver.Object;
        }
    }
}
