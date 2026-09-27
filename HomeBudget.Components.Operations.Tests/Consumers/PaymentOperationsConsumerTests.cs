using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
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
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Consumers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Options;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Services.Interfaces;
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
            dependencies.Inbox
                .Setup(x => x.MarkFailedAsync(
                    "message-42",
                    "eventstore down",
                    It.IsAny<int>(),
                    It.IsAny<DateTime>()))
                .ReturnsAsync(new PaymentInboxFailureResult
                {
                    MessageId = "message-42",
                    Status = PaymentInboxStatus.Failed,
                    RetryCount = 1
                });
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
            var stoppedActivities = new ConcurrentBag<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "HomeBudget.Accounting",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stoppedActivities.Add
            };
            ActivitySource.AddActivityListener(listener);
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
            dependencies.Inbox.Verify(
                x => x.MarkPoisonAsync(
                    "message-42",
                    It.IsAny<string>(),
                    It.IsAny<DateTime>()),
                Times.Once);
            dependencies.EventStore.Verify(
                x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            dependencies.KafkaConsumer.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
            var consumeActivity = stoppedActivities.Single(activity => activity.OperationName == "kafka.consume");
            consumeActivity.Status.Should().Be(ActivityStatusCode.Error);
            consumeActivity.Events.Should().Contain(activityEvent => activityEvent.Name == "exception");
        }

        [Test]
        public async Task ConsumeAsync_WhenProcessingThrows_ThenSurfacesExceptionToConsumerLoop()
        {
            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.Inbox
                .Setup(x => x.MarkFailedAsync(
                    "message-42",
                    "append failed",
                    It.IsAny<int>(),
                    It.IsAny<DateTime>()))
                .ReturnsAsync(new PaymentInboxFailureResult
                {
                    MessageId = "message-42",
                    Status = PaymentInboxStatus.Failed,
                    RetryCount = 1
                });
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
            dependencies.Inbox.Verify(
                x => x.MarkProcessedAsync("message-42", It.IsAny<DateTime>()),
                Times.Once);
        }

        [Test]
        public async Task ConsumeAsync_WhenKafkaCommitFails_ThenMarksConsumerSpanAsError()
        {
            var stoppedActivities = new ConcurrentBag<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "HomeBudget.Accounting",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stoppedActivities.Add
            };
            ActivitySource.AddActivityListener(listener);

            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<IWriteResult>());
            dependencies.KafkaConsumer
                .Setup(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()))
                .Throws(new KafkaException(new Error(ErrorCode.Local_Fail, "commit failed")));
            using var sut = dependencies.BuildConsumer();

            Func<Task> act = () => sut.ConsumeAsync(CancellationToken.None);

            await act.Should().ThrowAsync<KafkaException>();
            var consumeActivity = stoppedActivities.Single(activity => activity.OperationName == "kafka.consume");
            consumeActivity.Status.Should().Be(ActivityStatusCode.Error);
            consumeActivity.Events.Should().Contain(activityEvent => activityEvent.Name == "exception");
        }

        [Test]
        public async Task ConsumeAsync_WhenTraceHeadersArePresent_ThenPreservesCausalParentAcrossKafkaAndEventStore()
        {
            using var cancellation = new CancellationTokenSource();
            var stoppedActivities = new ConcurrentBag<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "HomeBudget.Accounting",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stoppedActivities.Add
            };
            ActivitySource.AddActivityListener(listener);

            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<PaymentOperationEvent>, string, string, CancellationToken>((events, _, _, _) =>
                {
                    var current = Activity.Current;
                    current.Should().NotBeNull();
                    current!.TraceId.ToString().Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
                    events.Should().OnlyContain(paymentEvent =>
                        paymentEvent.Metadata[EventMetadataKeys.TraceParent] == current.Id);
                    cancellation.Cancel();
                })
                .ReturnsAsync(Mock.Of<IWriteResult>());
            using var sut = dependencies.BuildConsumer();

            await sut.ConsumeAsync(cancellation.Token);

            var kafkaActivity = stoppedActivities.Single(activity => activity.OperationName == "kafka.consume");
            var processingActivity = stoppedActivities.Single(activity => activity.OperationName == "payment.process");
            kafkaActivity.ParentSpanId.ToString().Should().Be("00f067aa0ba902b7");
            processingActivity.ParentSpanId.Should().Be(kafkaActivity.SpanId);
            processingActivity.TraceId.Should().Be(kafkaActivity.TraceId);
        }

        [Test]
        public async Task ConsumeAsync_WhenProcessingSucceeds_ThenEmitsBoundedKafkaMetrics()
        {
            using var cancellation = new CancellationTokenSource();
            var measurements = new ConcurrentBag<(string Name, long Value, string Outcome)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "HomeBudget.Accounting.Metrics"
                    && instrument.Name is "homebudget.kafka.messages.received"
                        or "homebudget.kafka.processing.attempts"
                        or "homebudget.kafka.offset.commit.outcomes")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                var outcome = tags.ToArray()
                    .SingleOrDefault(tag => tag.Key == "outcome")
                    .Value?.ToString();
                measurements.Add((instrument.Name, measurement, outcome));
            });
            listener.Start();

            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => cancellation.Cancel())
                .ReturnsAsync(Mock.Of<IWriteResult>());
            using var sut = dependencies.BuildConsumer();

            await sut.ConsumeAsync(cancellation.Token);

            measurements.Should().ContainSingle(measurement =>
                measurement.Name == "homebudget.kafka.messages.received"
                && measurement.Value == 1
                && measurement.Outcome == null);
            measurements.Should().ContainSingle(measurement =>
                measurement.Name == "homebudget.kafka.processing.attempts"
                && measurement.Value == 1
                && measurement.Outcome == "persisted");
            measurements.Should().ContainSingle(measurement =>
                measurement.Name == "homebudget.kafka.offset.commit.outcomes"
                && measurement.Value == 1
                && measurement.Outcome == "success");
        }

        [Test]
        public async Task ConsumeAsync_WhenWorkerRestartsAfterUncommittedFailure_ThenMessageCanBeProcessedAndCommitted()
        {
            var consumeResult = BuildConsumeResult(BuildPaymentEventJson());
            var firstRun = BuildDependencies(consumeResult);
            firstRun.Inbox
                .Setup(x => x.MarkFailedAsync(
                    "message-42",
                    "first append failed",
                    It.IsAny<int>(),
                    It.IsAny<DateTime>()))
                .ReturnsAsync(new PaymentInboxFailureResult
                {
                    MessageId = "message-42",
                    Status = PaymentInboxStatus.Failed,
                    RetryCount = 1
                });
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
            secondRun.Inbox.Verify(
                x => x.MarkProcessedAsync("message-42", It.IsAny<DateTime>()),
                Times.Once);
        }

        [Test]
        public async Task ConsumeAsync_WhenMessageIdAlreadyProcessed_ThenSkipsEventStoreAndCommitsKafkaOffset()
        {
            using var cancellation = new CancellationTokenSource();
            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.Inbox
                .Setup(x => x.StartProcessingAsync(It.IsAny<PaymentInboxMessageEntity>()))
                .Callback(() => cancellation.Cancel())
                .ReturnsAsync(new PaymentInboxStartResult
                {
                    MessageId = "message-42",
                    Status = PaymentInboxStatus.Processed,
                    RetryCount = 0,
                    ShouldProcess = false
                });
            var sut = dependencies.BuildConsumer();

            await sut.ConsumeAsync(cancellation.Token);

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
        public async Task ConsumeAsync_WhenRetryLimitIsReached_ThenWritesDeadLetterAndCommitsKafkaOffset()
        {
            using var cancellation = new CancellationTokenSource();
            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("eventstore down"));
            dependencies.Inbox
                .Setup(x => x.MarkFailedAsync(
                    "message-42",
                    "eventstore down",
                    It.IsAny<int>(),
                    It.IsAny<DateTime>()))
                .ReturnsAsync(new PaymentInboxFailureResult
                {
                    MessageId = "message-42",
                    Status = PaymentInboxStatus.Poison,
                    RetryCount = 5
                });
            dependencies.EventStore
                .Setup(x => x.SendToDeadLetterQueueAsync(
                    It.IsAny<BaseEvent>(),
                    It.IsAny<Exception>()))
                .Callback(() => cancellation.Cancel())
                .Returns(Task.CompletedTask);
            var sut = dependencies.BuildConsumer();

            await sut.ConsumeAsync(cancellation.Token);

            dependencies.EventStore.Verify(
                x => x.SendToDeadLetterQueueAsync(
                    It.IsAny<BaseEvent>(),
                    It.IsAny<InvalidOperationException>()),
                Times.Once);
            dependencies.Inbox.Verify(
                x => x.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<DateTime>()),
                Times.Never);
            dependencies.KafkaConsumer.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
        }

        [Test]
        public async Task ConsumeAsync_WhenReplayRequestedMessageSucceeds_ThenMarksMessageProcessed()
        {
            using var cancellation = new CancellationTokenSource();
            var dependencies = BuildDependencies(BuildConsumeResult(BuildPaymentEventJson()));
            dependencies.Inbox
                .Setup(x => x.StartProcessingAsync(It.IsAny<PaymentInboxMessageEntity>()))
                .ReturnsAsync(new PaymentInboxStartResult
                {
                    MessageId = "message-42",
                    Status = PaymentInboxStatus.Processing,
                    RetryCount = 0,
                    ShouldProcess = true
                });
            dependencies.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<IWriteResult>());
            dependencies.Inbox
                .Setup(x => x.MarkProcessedAsync("message-42", It.IsAny<DateTime>()))
                .Callback(() => cancellation.Cancel())
                .Returns(Task.CompletedTask);
            var sut = dependencies.BuildConsumer();

            await sut.ConsumeAsync(cancellation.Token);

            dependencies.Inbox.Verify(
                x => x.MarkProcessedAsync("message-42", It.IsAny<DateTime>()),
                Times.Once);
            dependencies.KafkaConsumer.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
        }

        private static ConsumerDependencies BuildDependencies(ConsumeResult<string, string> consumeResult)
        {
            var kafkaConsumer = new Mock<IConsumer<string, string>>();
            kafkaConsumer
                .Setup(x => x.Consume(It.IsAny<TimeSpan>()))
                .Returns(consumeResult);

            var eventStore = new Mock<IEventStoreDbWriteClient<PaymentOperationEvent>>();
            var inbox = new Mock<IPaymentMessageInboxService>();
            inbox
                .Setup(x => x.StartProcessingAsync(It.IsAny<PaymentInboxMessageEntity>()))
                .ReturnsAsync(new PaymentInboxStartResult
                {
                    MessageId = "message-42",
                    Status = PaymentInboxStatus.Processing,
                    RetryCount = 0,
                    ShouldProcess = true
                });
            inbox
                .Setup(x => x.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<DateTime>()))
                .Returns(Task.CompletedTask);
            inbox
                .Setup(x => x.MarkPoisonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .Returns(Task.CompletedTask);
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider
                .Setup(x => x.GetNowUtc())
                .Returns(new DateTime(2026, 05, 09, 12, 00, 00, DateTimeKind.Utc));

            return new ConsumerDependencies(kafkaConsumer, eventStore, inbox, dateTimeProvider);
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
            Mock<IPaymentMessageInboxService> inbox,
            Mock<IDateTimeProvider> dateTimeProvider)
        {
            public Mock<IConsumer<string, string>> KafkaConsumer { get; } = kafkaConsumer;

            public Mock<IEventStoreDbWriteClient<PaymentOperationEvent>> EventStore { get; } = eventStore;

            public Mock<IPaymentMessageInboxService> Inbox { get; } = inbox;

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
                    Microsoft.Extensions.Options.Options.Create(new PaymentInboxOptions
                    {
                        MaxRetryAttempts = 5
                    }),
                    Inbox.Object,
                    KafkaConsumer.Object);
            }
        }
    }
}
