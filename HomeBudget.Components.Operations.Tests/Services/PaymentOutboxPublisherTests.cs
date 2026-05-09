using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Options;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public class PaymentOutboxPublisherTests
    {
        [Test]
        public async Task PublishRetryableRowsAsync_WhenKafkaPublishSucceeds_ThenMarksPublished()
        {
            var row = BuildOutboxRow(OutboxStatus.Pending);
            var dependencies = BuildDependencies([row]);
            dependencies.KafkaProducer
                .Setup(x => x.ProduceAsync(
                    It.IsAny<string>(),
                    It.IsAny<Message<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeliveryResult<string, string>());
            var sut = dependencies.BuildPublisher();

            var publishedCount = await sut.PublishRetryableRowsAsync(CancellationToken.None);

            publishedCount.Should().Be(1);
            dependencies.Outbox.Verify(
                x => x.MarkPublishedAsync(row.MessageId, It.IsAny<string>(), dependencies.NowUtc),
                Times.Once);
            dependencies.Outbox.Verify(
                x => x.MarkFailedAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<DateTime>()),
                Times.Never);
        }

        [Test]
        public async Task PublishRetryableRowsAsync_WhenKafkaIsDown_ThenMarksFailedWithRetryMetadata()
        {
            var row = BuildOutboxRow(OutboxStatus.Pending);
            var dependencies = BuildDependencies([row]);
            dependencies.KafkaProducer
                .Setup(x => x.ProduceAsync(
                    It.IsAny<string>(),
                    It.IsAny<Message<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("kafka down"));
            var sut = dependencies.BuildPublisher();

            var publishedCount = await sut.PublishRetryableRowsAsync(CancellationToken.None);

            publishedCount.Should().Be(0);
            dependencies.Outbox.Verify(
                x => x.MarkFailedAsync(
                    row.MessageId,
                    It.IsAny<string>(),
                    "kafka down",
                    dependencies.Options.MaxRetryAttempts,
                    dependencies.NowUtc),
                Times.Once);
            dependencies.Outbox.Verify(
                x => x.MarkPublishedAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>()),
                Times.Never);
        }

        [Test]
        public async Task PublishRetryableRowsAsync_WhenFailedRowIsRetryable_ThenPublishesLater()
        {
            var row = BuildOutboxRow(OutboxStatus.Failed);
            var dependencies = BuildDependencies([row]);
            dependencies.KafkaProducer
                .Setup(x => x.ProduceAsync(
                    It.IsAny<string>(),
                    It.IsAny<Message<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeliveryResult<string, string>());
            var sut = dependencies.BuildPublisher();

            var publishedCount = await sut.PublishRetryableRowsAsync(CancellationToken.None);

            publishedCount.Should().Be(1);
            dependencies.Outbox.Verify(
                x => x.LockRetryableRowsAsync(
                    It.IsAny<string>(),
                    dependencies.NowUtc,
                    dependencies.NowUtc.AddSeconds(dependencies.Options.LockTimeoutSeconds),
                    dependencies.Options.BatchSize,
                    dependencies.Options.MaxRetryAttempts),
                Times.Once);
            dependencies.Outbox.Verify(
                x => x.MarkPublishedAsync(row.MessageId, It.IsAny<string>(), dependencies.NowUtc),
                Times.Once);
        }

        private static OutboxPublisherDependencies BuildDependencies(IReadOnlyCollection<OutboxAccountPaymentsEntity> rows)
        {
            var nowUtc = new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc);
            var options = new PaymentOutboxPublisherOptions
            {
                BatchSize = 10,
                LockTimeoutSeconds = 30,
                MaxRetryAttempts = 3
            };
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox
                .Setup(x => x.LockRetryableRowsAsync(
                    It.IsAny<string>(),
                    nowUtc,
                    nowUtc.AddSeconds(options.LockTimeoutSeconds),
                    options.BatchSize,
                    options.MaxRetryAttempts))
                .ReturnsAsync(rows);

            var kafkaProducer = new Mock<IKafkaProducer<string, string>>();
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider
                .Setup(x => x.GetNowUtc())
                .Returns(nowUtc);

            return new OutboxPublisherDependencies(
                outbox,
                kafkaProducer,
                dateTimeProvider,
                options,
                nowUtc);
        }

        private static OutboxAccountPaymentsEntity BuildOutboxRow(OutboxStatus status)
        {
            var operationId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var paymentEvent = new PaymentOperationEvent
            {
                EventType = PaymentEventTypes.Added,
                EnvelopId = Guid.NewGuid(),
                Payload = new FinancialTransaction
                {
                    Key = operationId,
                    PaymentAccountId = accountId,
                    OperationDay = new DateOnly(2026, 05, 09),
                    Amount = 25m
                }
            };

            return new OutboxAccountPaymentsEntity
            {
                AggregateId = accountId.ToString(),
                OperationId = operationId.ToString(),
                EventType = paymentEvent.EventType.ToString(),
                MessageId = paymentEvent.EnvelopId.ToString(),
                PartitionKey = $"{accountId}-{operationId}",
                Payload = JsonSerializer.Serialize(paymentEvent),
                CreatedAt = new DateTime(2026, 05, 09, 11, 59, 0, DateTimeKind.Utc),
                CreatedUtc = new DateTime(2026, 05, 09, 11, 59, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 05, 09, 11, 59, 0, DateTimeKind.Utc),
                UpdatedUtc = new DateTime(2026, 05, 09, 11, 59, 0, DateTimeKind.Utc),
                Status = status.Key
            };
        }

        private sealed class OutboxPublisherDependencies(
            Mock<IOutboxPaymentStatusService> outbox,
            Mock<IKafkaProducer<string, string>> kafkaProducer,
            Mock<IDateTimeProvider> dateTimeProvider,
            PaymentOutboxPublisherOptions options,
            DateTime nowUtc)
        {
            public Mock<IOutboxPaymentStatusService> Outbox { get; } = outbox;

            public Mock<IKafkaProducer<string, string>> KafkaProducer { get; } = kafkaProducer;

            public PaymentOutboxPublisherOptions Options { get; } = options;

            public DateTime NowUtc { get; } = nowUtc;

            public PaymentOutboxPublisher BuildPublisher()
            {
                return new PaymentOutboxPublisher(
                    Mock.Of<ILogger<PaymentOutboxPublisher>>(),
                    Outbox.Object,
                    KafkaProducer.Object,
                    dateTimeProvider.Object,
                    Microsoft.Extensions.Options.Options.Create(Options));
            }
        }
    }
}
