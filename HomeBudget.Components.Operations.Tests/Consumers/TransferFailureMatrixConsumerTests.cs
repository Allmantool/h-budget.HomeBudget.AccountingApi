using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
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
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Tests.Consumers
{
    [TestFixture]
    internal sealed class TransferFailureMatrixConsumerTests
    {
        private static readonly Guid TransferId = Guid.Parse("783467d4-7101-40ea-9ad7-392f9ec12619");
        private static readonly Guid SenderAccountId = Guid.Parse("975a222c-c606-4608-9ef9-7d42a43bba90");
        private static readonly Guid RecipientAccountId = Guid.Parse("0dd44772-7f1a-455d-a5e7-513b0eec5b52");

        [TestCase("XFER-006", true, -1800)]
        [TestCase("XFER-007", false, 6030)]
        public async Task EventStoreTransientFailureRedeliversTheSameExactTransferSide(
            string scenarioId,
            bool sender,
            decimal expectedAmount)
        {
            var messageId = $"{scenarioId}-message";
            var message = BuildConsumeResult(messageId, sender);
            using var first = BuildDependencies(message);
            first.Inbox.Setup(x => x.MarkFailedAsync(messageId, "eventstore unavailable", 5, It.IsAny<DateTime>()))
                .ReturnsAsync(new PaymentInboxFailureResult
                {
                    MessageId = messageId,
                    Status = PaymentInboxStatus.Failed,
                    RetryCount = 1
                });
            first.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("eventstore unavailable"));

            Func<Task> firstAttempt = () => first.Consumer.ConsumeAsync(CancellationToken.None);
            await firstAttempt.Should().ThrowAsync<InvalidOperationException>();
            first.Kafka.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
            first.Inbox.Verify(
                x => x.MarkFailedAsync(messageId, "eventstore unavailable", 5, It.IsAny<DateTime>()),
                Times.Once);

            PaymentOperationEvent appended = null;
            using var cancellation = new CancellationTokenSource();
            using var retry = BuildDependencies(message);
            retry.EventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<PaymentOperationEvent>, string, string, CancellationToken>((events, _, _, _) =>
                {
                    appended = events.Single();
                    cancellation.Cancel();
                })
                .ReturnsAsync(Mock.Of<IWriteResult>());

            await retry.Consumer.ConsumeAsync(cancellation.Token);

            appended.Should().NotBeNull();
            appended.Payload.Key.Should().Be(TransferId);
            appended.Payload.PaymentAccountId.Should().Be(sender ? SenderAccountId : RecipientAccountId);
            appended.Payload.Amount.Should().Be(expectedAmount);
            appended.Payload.TransactionType.Should().Be(TransactionTypes.Transfer);
            retry.EventStore.Verify(
                x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            retry.Kafka.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
        }

        [TestCase("XFER-008", true, -1800)]
        [TestCase("XFER-009", false, 6030)]
        public async Task DuplicateDeliveryAppendsTheExactTransferSideOnlyOnce(
            string scenarioId,
            bool sender,
            decimal expectedAmount)
        {
            var messageId = $"{scenarioId}-message";
            var message = BuildConsumeResult(messageId, sender);
            var eventStore = new Mock<IEventStoreDbWriteClient<PaymentOperationEvent>>();
            PaymentOperationEvent appended = null;
            using var firstCancellation = new CancellationTokenSource();
            eventStore
                .Setup(x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<PaymentOperationEvent>, string, string, CancellationToken>((events, _, _, _) =>
                {
                    appended = events.Single();
                    firstCancellation.Cancel();
                })
                .ReturnsAsync(Mock.Of<IWriteResult>());
            using var first = BuildDependencies(message, eventStore);

            await first.Consumer.ConsumeAsync(firstCancellation.Token);

            using var duplicateCancellation = new CancellationTokenSource();
            using var duplicate = BuildDependencies(message, eventStore);
            duplicate.Inbox
                .Setup(x => x.StartProcessingAsync(It.IsAny<PaymentInboxMessageEntity>()))
                .Callback(() => duplicateCancellation.Cancel())
                .ReturnsAsync(new PaymentInboxStartResult
                {
                    MessageId = messageId,
                    Status = PaymentInboxStatus.Processed,
                    ShouldProcess = false
                });

            await duplicate.Consumer.ConsumeAsync(duplicateCancellation.Token);

            appended.Should().NotBeNull();
            appended.Payload.Key.Should().Be(TransferId);
            appended.Payload.PaymentAccountId.Should().Be(sender ? SenderAccountId : RecipientAccountId);
            appended.Payload.Amount.Should().Be(expectedAmount);
            eventStore.Verify(
                x => x.SendBatchAsync(
                    It.IsAny<IEnumerable<PaymentOperationEvent>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            first.Kafka.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
            duplicate.Kafka.Verify(x => x.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Once);
        }

        private static Dependencies BuildDependencies(
            ConsumeResult<string, string> message,
            Mock<IEventStoreDbWriteClient<PaymentOperationEvent>> eventStore = null)
        {
            var kafka = new Mock<IConsumer<string, string>>();
            kafka.Setup(x => x.Consume(It.IsAny<TimeSpan>())).Returns(message);
            var inbox = new Mock<IPaymentMessageInboxService>();
            inbox.Setup(x => x.StartProcessingAsync(It.IsAny<PaymentInboxMessageEntity>()))
                .ReturnsAsync(new PaymentInboxStartResult
                {
                    MessageId = Encoding.UTF8.GetString(message.Message.Headers.GetLastBytes(KafkaMessageHeaders.MessageId)),
                    Status = PaymentInboxStatus.Processing,
                    ShouldProcess = true
                });
            inbox.Setup(x => x.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<DateTime>()))
                .Returns(Task.CompletedTask);
            var clock = new Mock<IDateTimeProvider>();
            clock.Setup(x => x.GetNowUtc()).Returns(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));
            eventStore ??= new Mock<IEventStoreDbWriteClient<PaymentOperationEvent>>();
            return new Dependencies(kafka, eventStore, inbox, clock);
        }

        private static ConsumeResult<string, string> BuildConsumeResult(string messageId, bool sender)
        {
            var headers = new Headers
            {
                { KafkaMessageHeaders.MessageId, Encoding.UTF8.GetBytes(messageId) },
                { KafkaMessageHeaders.CorrelationId, Encoding.UTF8.GetBytes(TransferId.ToString()) }
            };

            return new ConsumeResult<string, string>
            {
                Topic = BaseTopics.AccountingPayments,
                Partition = new Partition(sender ? 0 : 1),
                Offset = new Offset(42),
                Message = new Message<string, string>
                {
                    Key = messageId,
                    Headers = headers,
                    Value = BuildTransferEventJson(sender)
                }
            };
        }

        private static string BuildTransferEventJson(bool sender)
        {
            var paymentEvent = new PaymentOperationEvent
            {
                EventType = PaymentEventTypes.Added,
                Payload = new FinancialTransaction
                {
                    Key = TransferId,
                    PaymentAccountId = sender ? SenderAccountId : RecipientAccountId,
                    OperationDay = new DateOnly(2025, 1, 24),
                    Amount = sender ? -1800m : 6030m,
                    ConversionMultiplier = 3.35m,
                    TransactionType = TransactionTypes.Transfer
                }
            };
            return JsonSerializer.Serialize(paymentEvent);
        }

        private sealed class Dependencies : IDisposable
        {
            public Dependencies(
                Mock<IConsumer<string, string>> kafka,
                Mock<IEventStoreDbWriteClient<PaymentOperationEvent>> eventStore,
                Mock<IPaymentMessageInboxService> inbox,
                Mock<IDateTimeProvider> clock)
            {
                Kafka = kafka;
                EventStore = eventStore;
                Inbox = inbox;
                Consumer = new PaymentOperationsConsumer(
                    Mock.Of<ILogger<PaymentOperationsConsumer>>(),
                    clock.Object,
                    eventStore.Object,
                    Microsoft.Extensions.Options.Options.Create(new KafkaOptions
                    {
                        ConsumerSettings = new ConsumerSettings
                        {
                            BootstrapServers = "localhost:9092",
                            ConsumeDelayInMilliseconds = 1,
                            HeartbeatIntervalMs = 1
                        }
                    }),
                    Microsoft.Extensions.Options.Options.Create(new PaymentInboxOptions { MaxRetryAttempts = 5 }),
                    inbox.Object,
                    kafka.Object);
            }

            public PaymentOperationsConsumer Consumer { get; }

            public Mock<IConsumer<string, string>> Kafka { get; }

            public Mock<IEventStoreDbWriteClient<PaymentOperationEvent>> EventStore { get; }

            public Mock<IPaymentMessageInboxService> Inbox { get; }

            public void Dispose() => Consumer.Dispose();
        }
    }
}
