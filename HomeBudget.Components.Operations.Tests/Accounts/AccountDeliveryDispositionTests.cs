using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;
using HomeBudget.Components.Accounts.Consumers;
using HomeBudget.Components.Accounts.Handlers;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Tests.Accounts
{
    [TestFixture]
    internal sealed class AccountDeliveryDispositionTests
    {
        private static readonly LedgerGenerationScope _active =
            new("default", "replacement-2", 2, "\"generation-2\"");

        [Test]
        public async Task ConsumeAsync_WhenDispositionWriteCrashes_DoesNotCommitOrMutateAccount()
        {
            using var cancellation = new CancellationTokenSource();
            var delivery = CreateDelivery("message-42", "default", "legacy");
            var kafka = CreateKafka(delivery);
            var handler = new RecordingAccountOperationsHandler();
            var store = new Mock<IAccountDeliveryDispositionStore>();
            store
                .Setup(value => value.RecordAsync(
                    It.IsAny<AccountDeliveryDisposition>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("simulated disposition-store crash"));
            using var sut = CreateConsumer(handler, store.Object, kafka.Object);

            var act = async () => await sut.ConsumeAsync(cancellation.Token);

            await act.Should().ThrowAsync<IOException>();
            kafka.Verify(value => value.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
            handler.CallCount.Should().Be(0);
        }

        [Test]
        public async Task ConsumeAsync_WhenIdenticalStaleDeliveryIsRedelivered_CommitsAfterDurableDispositionWithoutEffect()
        {
            using var cancellation = new CancellationTokenSource();
            var delivery = CreateDelivery("message-42", "default", "legacy");
            var kafka = CreateKafka(delivery);
            kafka
                .Setup(value => value.Commit(It.IsAny<ConsumeResult<string, string>>()))
                .Callback(cancellation.Cancel);
            var handler = new RecordingAccountOperationsHandler();
            var store = new Mock<IAccountDeliveryDispositionStore>();
            var dispositionRecorded = false;
            store
                .Setup(value => value.RecordAsync(
                    It.IsAny<AccountDeliveryDisposition>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => dispositionRecorded = true)
                .ReturnsAsync(AccountDeliveryDispositionResult.AlreadyRecorded);
            kafka
                .Setup(value => value.Commit(It.IsAny<ConsumeResult<string, string>>()))
                .Callback(() =>
                {
                    dispositionRecorded.Should().BeTrue(
                        "Kafka acknowledgement must follow the durable terminal disposition");
                    cancellation.Cancel();
                });
            using var sut = CreateConsumer(handler, store.Object, kafka.Object);

            await sut.ConsumeAsync(cancellation.Token);

            store.Verify(
                value => value.RecordAsync(
                    It.Is<AccountDeliveryDisposition>(disposition =>
                        disposition.MessageId == "message-42"
                        && disposition.LedgerId == "default"
                        && disposition.LedgerGenerationId == "legacy"
                        && disposition.Reason == "stale-generation"
                        && disposition.PayloadSha256.Length == 64),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            kafka.Verify(value => value.Commit(delivery), Times.Once);
            handler.CallCount.Should().Be(0);
        }

        [Test]
        public async Task ConsumeAsync_WhenSameIdentityHasChangedPayload_DoesNotCommitOrMutateAccount()
        {
            using var cancellation = new CancellationTokenSource();
            var delivery = CreateDelivery("message-42", "default", "legacy", Guid.NewGuid());
            var kafka = CreateKafka(delivery);
            kafka
                .Setup(value => value.Commit(It.IsAny<ConsumeResult<string, string>>()))
                .Callback(cancellation.Cancel);
            var handler = new RecordingAccountOperationsHandler();
            var store = new Mock<IAccountDeliveryDispositionStore>();
            store
                .Setup(value => value.RecordAsync(
                    It.IsAny<AccountDeliveryDisposition>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AccountDeliveryDispositionConflictException("payload conflict"));
            using var sut = CreateConsumer(handler, store.Object, kafka.Object);

            var act = async () => await sut.ConsumeAsync(cancellation.Token);

            await act.Should().ThrowAsync<AccountDeliveryDispositionConflictException>();
            kafka.Verify(value => value.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
            handler.CallCount.Should().Be(0);
        }

        private static AccountOperationsConsumer CreateConsumer(
            IAccountOperationsHandler handler,
            IAccountDeliveryDispositionStore store,
            IConsumer<string, string> kafka)
        {
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver
                .Setup(value => value.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_active);
            return new AccountOperationsConsumer(
                NullLogger<AccountOperationsConsumer>.Instance,
                handler,
                Microsoft.Extensions.Options.Options.Create(CreateKafkaOptions()),
                resolver.Object,
                store,
                CreateAdmissionFence(),
                kafka);
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

        private static Mock<IConsumer<string, string>> CreateKafka(ConsumeResult<string, string> delivery)
        {
            var kafka = new Mock<IConsumer<string, string>>();
            kafka.Setup(value => value.Consume(It.IsAny<TimeSpan>())).Returns(delivery);
            return kafka;
        }

        private static ConsumeResult<string, string> CreateDelivery(
            string messageId,
            string ledgerId,
            string ledgerGenerationId,
            Guid? accountId = null)
        {
            var accountEvent = new AccountOperationEvent
            {
                Payload = new HomeBudget.Accounting.Domain.Models.PaymentAccount
                {
                    Key = accountId ?? Guid.Parse("79de43fb-d983-4aa0-bd42-8e1a7800b7fe")
                }
            };
            var headers = new Headers
            {
                new(KafkaMessageHeaders.MessageId, Encoding.UTF8.GetBytes(messageId)),
                new(KafkaMessageHeaders.LedgerId, Encoding.UTF8.GetBytes(ledgerId)),
                new(KafkaMessageHeaders.LedgerGenerationId, Encoding.UTF8.GetBytes(ledgerGenerationId))
            };
            return new ConsumeResult<string, string>
            {
                Topic = "accounting.accounts",
                Partition = new Partition(1),
                Offset = new Offset(42),
                Message = new Message<string, string>
                {
                    Key = "account-key",
                    Value = JsonSerializer.Serialize(accountEvent),
                    Headers = headers
                }
            };
        }

        private static KafkaOptions CreateKafkaOptions() => new()
        {
            ConsumerSettings = new ConsumerSettings
            {
                BootstrapServers = "127.0.0.1:1",
                ClientId = "account-disposition-test",
                GroupId = "account-disposition-test",
                Debug = null
            }
        };

        private sealed class RecordingAccountOperationsHandler : IAccountOperationsHandler
        {
            public int CallCount { get; private set; }

            public Task HandleAsync(AccountOperationEvent paymentEvent, CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.CompletedTask;
            }
        }
    }
}
