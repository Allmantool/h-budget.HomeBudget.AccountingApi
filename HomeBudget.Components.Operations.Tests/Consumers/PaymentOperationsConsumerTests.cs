using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Consumers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Tests.Consumers
{
    [TestFixture]
    public class PaymentOperationsConsumerTests
    {
        [Test]
        public async Task ConsumeAsync_WhenEventStoreAppendFails_ThenDoesNotCommitKafkaOffset()
        {
            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("eventstore down"));
            var sut = dependencies.BuildConsumer();

            Func<Task> act = () => sut.ConsumeAsync(CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            dependencies.KafkaConsumer.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
        }

        [Test]
        public async Task ConsumeAsync_WhenMessageCannotBeDeserialized_ThenWritesDeadLetterAndCommitsKafkaOffset()
        {
            using var cancellation = new CancellationTokenSource();
            var dependencies = BuildDependencies(BuildConsumeResult("{ not-json }"));
            BaseEvent deadLetterEvent = null;
            Exception deadLetterException = null;
            dependencies.EventStore
                .Setup(x => x.SendToDeadLetterQueueAsync(
                    It.IsAny<BaseEvent>(),
                    It.IsAny<Exception>()))
                .Callback<BaseEvent, Exception>((evt, ex) =>
                {
                    deadLetterEvent = evt;
                    deadLetterException = ex;
                    cancellation.Cancel();
                })
                .Returns(Task.CompletedTask);
            var sut = dependencies.BuildConsumer();

            await sut.ConsumeAsync(cancellation.Token);

            deadLetterException.Should().BeOfType<JsonException>();
            deadLetterEvent.Should().NotBeNull();
            deadLetterEvent!.Metadata[EventMetadataKeys.FromMessage].Should().Be("message-key");
            deadLetterEvent.Metadata[EventMetadataKeys.CorrelationId].Should().Be("correlation-42");
            deadLetterEvent.Metadata[EventMetadataKeys.TraceParent].Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00");
            deadLetterEvent.Metadata["raw-message"].Should().Be("{ not-json }");
            dependencies.EventStore.Verify(
                x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            dependencies.KafkaConsumer.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
        }

        [Test]
        public async Task ConsumeAsync_WhenProcessingThrows_ThenSurfacesExceptionToConsumerLoop()
        {
            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("append failed"));
            var sut = dependencies.BuildConsumer();

            Func<Task> act = () => sut.ConsumeAsync(CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("append failed");
        }

        [Test]
        public async Task ConsumeAsync_WhenEventStoreAppendSucceeds_ThenCommitsKafkaOffset()
        {
            using var cancellation = new CancellationTokenSource();
            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => cancellation.Cancel())
                .ReturnsAsync(Mock.Of<IWriteResult>());
            var sut = dependencies.BuildConsumer();

            await sut.ConsumeAsync(cancellation.Token);

            dependencies.EventStore.Verify(
                x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.Is<string>(stream => stream.Contains("11111111-1111-1111-1111-111111111111")),
                    It.Is<string>(eventType => eventType.StartsWith("Added_", StringComparison.Ordinal)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            dependencies.KafkaConsumer.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
        }

        [Test]
        public async Task ConsumeAsync_WhenWorkerRestartsAfterUncommittedFailure_ThenMessageCanBeProcessedAndCommitted()
        {
            var consumeResult = BuildConsumeResult(BuildPaymentEventJson());
            var firstRun = BuildDependencies(consumeResult);
            firstRun.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("first append failed"));
            var firstConsumer = firstRun.BuildConsumer();

            Func<Task> firstAct = () => firstConsumer.ConsumeAsync(CancellationToken.None);

            await firstAct.Should().ThrowAsync<InvalidOperationException>();
            firstRun.KafkaConsumer.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);

            using var secondRunCancellation = new CancellationTokenSource();
            var secondRun = BuildDependencies(consumeResult);
            secondRun.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => secondRunCancellation.Cancel())
                .ReturnsAsync(Mock.Of<IWriteResult>());
            var restartedConsumer = secondRun.BuildConsumer();

            await restartedConsumer.ConsumeAsync(secondRunCancellation.Token);

            secondRun.KafkaConsumer.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
        }

        private static ConsumerDependencies BuildDependencies(ConsumeResult<string, string> consumeResult)
        {
            var kafkaConsumer = new Mock<IConsumer<string, string>>();
            kafkaConsumer
                .Setup(x => x.Consume(It.IsAny<TimeSpan>()))
                .Returns(consumeResult);

            var eventStore = new Mock<IEventStoreDbWriteClient<PaymentOperationEvent>>();
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider
                .Setup(x => x.GetNowUtc())
                .Returns(new DateTime(2026, 05, 09, 12, 00, 00, DateTimeKind.Utc));

            return new ConsumerDependencies(kafkaConsumer, eventStore, dateTimeProvider);
        }

        private static ConsumeResult<string, string> BuildConsumeResult(string value)
        {
            var headers = new Headers
            {
                { KafkaMessageHeaders.CorrelationId, Encoding.UTF8.GetBytes("correlation-42") },
                { KafkaMessageHeaders.MessageId, Encoding.UTF8.GetBytes("message-42") },
                { KafkaMessageHeaders.CausationId, Encoding.UTF8.GetBytes("cause-42") },
                { KafkaMessageHeaders.TraceId, Encoding.UTF8.GetBytes("trace-42") },
                { KafkaMessageHeaders.Traceparent, Encoding.UTF8.GetBytes("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00") },
                { KafkaMessageHeaders.Tracestate, Encoding.UTF8.GetBytes("state-42") },
                { KafkaMessageHeaders.Baggage, Encoding.UTF8.GetBytes("tenant=home") }
            };

            return new ConsumeResult<string, string>
            {
                Topic = "accounting.payments",
                Partition = new Partition(2),
                Offset = new Offset(10),
                Message = new Message<string, string>
                {
                    Key = "message-key",
                    Value = value,
                    Headers = headers
                }
            };
        }

        private static string BuildPaymentEventJson()
        {
            var paymentEvent = new PaymentOperationEvent
            {
                EventType = PaymentEventTypes.Added,
                Payload = new FinancialTransaction
                {
                    Key = new Guid("22222222-2222-2222-2222-222222222222"),
                    PaymentAccountId = new Guid("11111111-1111-1111-1111-111111111111"),
                    OperationDay = new DateOnly(2026, 05, 09),
                    Amount = 25m
                }
            };
            paymentEvent.Metadata[EventMetadataKeys.CorrelationId] = "correlation-42";

            return JsonSerializer.Serialize(paymentEvent);
        }

        private sealed class ConsumerDependencies(
            Mock<IConsumer<string, string>> kafkaConsumer,
            Mock<IEventStoreDbWriteClient<PaymentOperationEvent>> eventStore,
            Mock<IDateTimeProvider> dateTimeProvider)
        {
            public Mock<IConsumer<string, string>> KafkaConsumer { get; } = kafkaConsumer;

            public Mock<IEventStoreDbWriteClient<PaymentOperationEvent>> EventStore { get; } = eventStore;

            public PaymentOperationsConsumer BuildConsumer()
            {
                var options = Microsoft.Extensions.Options.Options.Create(new KafkaOptions
                {
                    ConsumerSettings = new ConsumerSettings
                    {
                        BootstrapServers = "localhost:9092",
                        ConsumeDelayInMilliseconds = 1,
                        HeartbeatIntervalMs = 1
                    }
                });

                return new PaymentOperationsConsumer(
                    Mock.Of<ILogger<PaymentOperationsConsumer>>(),
                    dateTimeProvider.Object,
                    EventStore.Object,
                    options,
                    KafkaConsumer.Object);
            }
        }
    }
}
