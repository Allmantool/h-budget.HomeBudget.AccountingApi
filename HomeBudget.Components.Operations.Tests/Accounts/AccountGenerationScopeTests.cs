using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Accounts.Consumers;
using HomeBudget.Components.Accounts.Handlers;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Components.Accounts.Services;
using HomeBudget.Core.Options;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Tests.Accounts
{
    [TestFixture]
    internal class AccountGenerationScopeTests
    {
        [Test]
        public async Task SendAsync_StampsServerResolvedGenerationHeaders()
        {
            Message<string, string> published = null;
            var producer = new Mock<IKafkaProducer<string, string>>();
            producer
                .Setup(value => value.ProduceAsync(
                    It.IsAny<string>(),
                    It.IsAny<Message<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, Message<string, string>, CancellationToken>((_, message, _) => published = message)
                .ReturnsAsync(new DeliveryResult<string, string>());
            var clock = new Mock<IDateTimeProvider>();
            clock.Setup(value => value.GetNowUtc()).Returns(new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc));
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver
                .Setup(value => value.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerGenerationScope("family", "replacement-2", 2, "\"generation-2\""));
            var sut = new PaymentAccountProducerService(
                NullLogger<PaymentAccountProducerService>.Instance,
                clock.Object,
                producer.Object,
                resolver.Object);

            await sut.SendAsync(new AccountRecord(Guid.NewGuid()), CancellationToken.None);

            ReadHeader(published, KafkaMessageHeaders.LedgerId).Should().Be("family");
            ReadHeader(published, KafkaMessageHeaders.LedgerGenerationId).Should().Be("replacement-2");
        }

        [Test]
        public async Task ProcessMessageAsync_WhenGenerationIsStale_IgnoresDeliveryWithoutAccountMutation()
        {
            var handler = new RecordingAccountOperationsHandler();
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver
                .Setup(value => value.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerGenerationScope("default", "replacement-2", 2, "\"generation-2\""));
            using var sut = new AccountOperationsConsumer(
                NullLogger<AccountOperationsConsumer>.Instance,
                handler,
                Microsoft.Extensions.Options.Options.Create(CreateKafkaOptions()),
                resolver.Object,
                Mock.Of<IAccountDeliveryDispositionStore>(),
                CreateAdmissionFence());

            var outcome = await sut.ProcessMessageAsync(
                CreateDelivery("default", "legacy"),
                CancellationToken.None);

            outcome.Should().Be(KafkaMessageProcessingOutcome.Duplicate);
            handler.CallCount.Should().Be(0);
        }

        [Test]
        public async Task ProcessMessageAsync_WhenGenerationIsActive_PropagatesScopeToHandler()
        {
            var handler = new RecordingAccountOperationsHandler();
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver
                .Setup(value => value.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerGenerationScope("family", "replacement-2", 2, "\"generation-2\""));
            using var sut = new AccountOperationsConsumer(
                NullLogger<AccountOperationsConsumer>.Instance,
                handler,
                Microsoft.Extensions.Options.Options.Create(CreateKafkaOptions()),
                resolver.Object,
                Mock.Of<IAccountDeliveryDispositionStore>(),
                CreateAdmissionFence());

            var outcome = await sut.ProcessMessageAsync(
                CreateDelivery("family", "replacement-2"),
                CancellationToken.None);

            outcome.Should().Be(KafkaMessageProcessingOutcome.Processed);
            handler.Handled.Metadata[HomeBudget.Core.Constants.EventMetadataKeys.LedgerId].Should().Be("family");
            handler.Handled.Metadata[HomeBudget.Core.Constants.EventMetadataKeys.LedgerGenerationId].Should().Be("replacement-2");
        }

        [Test]
        public async Task ProcessMessageAsync_WhenMaintenanceAdmissionIsClosed_DoesNotMutateAccount()
        {
            var handler = new RecordingAccountOperationsHandler();
            var active = new LedgerGenerationScope("family", "replacement-2", 2, "\"generation-2\"");
            var fence = new Mock<IMaintenanceAdmissionFence>();
            fence.Setup(value => value.AcquireAdmissionPermitAsync(
                    MaintenanceScopeKey.ForLedger(active),
                    active.LedgerGenerationId,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MaintenanceAdmissionClosedException());
            using var sut = CreateConsumer(handler, active, fence.Object);

            var act = () => sut.ProcessMessageAsync(
                CreateDelivery(active.LedgerId, active.LedgerGenerationId),
                CancellationToken.None);

            await act.Should().ThrowAsync<MaintenanceAdmissionClosedException>();
            handler.CallCount.Should().Be(0);
        }

        [Test]
        public async Task ProcessMessageAsync_WhenScopeIsMissing_AdoptsOnlyWhileLegacyGenerationIsActive()
        {
            var legacyHandler = new RecordingAccountOperationsHandler();
            using (var legacyConsumer = CreateConsumer(legacyHandler, LedgerGenerationScope.Legacy))
            {
                (await legacyConsumer.ProcessMessageAsync(CreateDelivery(), CancellationToken.None))
                    .Should().Be(KafkaMessageProcessingOutcome.Processed);
            }

            var replacementHandler = new RecordingAccountOperationsHandler();
            var replacement = new LedgerGenerationScope("default", "replacement-2", 2, "\"generation-2\"");
            using (var replacementConsumer = CreateConsumer(replacementHandler, replacement))
            {
                (await replacementConsumer.ProcessMessageAsync(CreateDelivery(), CancellationToken.None))
                    .Should().Be(KafkaMessageProcessingOutcome.Duplicate);
            }

            legacyHandler.CallCount.Should().Be(1);
            replacementHandler.CallCount.Should().Be(0);
        }

        private static string ReadHeader(Message<string, string> message, string name)
        {
            message.Should().NotBeNull();
            message.Headers.TryGetLastBytes(name, out var value).Should().BeTrue();
            return Encoding.UTF8.GetString(value);
        }

        private static AccountOperationsConsumer CreateConsumer(
            IAccountOperationsHandler handler,
            LedgerGenerationScope active,
            IMaintenanceAdmissionFence admissionFence = null)
        {
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver
                .Setup(value => value.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(active);
            return new AccountOperationsConsumer(
                NullLogger<AccountOperationsConsumer>.Instance,
                handler,
                Microsoft.Extensions.Options.Options.Create(CreateKafkaOptions()),
                resolver.Object,
                Mock.Of<IAccountDeliveryDispositionStore>(),
                admissionFence ?? CreateAdmissionFence());
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

        private static ConsumeResult<string, string> CreateDelivery(
            string ledgerId = null,
            string ledgerGenerationId = null)
        {
            var accountEvent = new AccountOperationEvent
            {
                Payload = new HomeBudget.Accounting.Domain.Models.PaymentAccount { Key = Guid.NewGuid() }
            };
            return new ConsumeResult<string, string>
            {
                Message = new Message<string, string>
                {
                    Key = Guid.NewGuid().ToString("N"),
                    Value = System.Text.Json.JsonSerializer.Serialize(accountEvent),
                    Headers = CreateScopeHeaders(ledgerId, ledgerGenerationId)
                }
            };
        }

        private static Headers CreateScopeHeaders(string ledgerId, string ledgerGenerationId)
        {
            var headers = new Headers();
            if (ledgerId is not null)
            {
                headers.Add(KafkaMessageHeaders.LedgerId, Encoding.UTF8.GetBytes(ledgerId));
            }

            if (ledgerGenerationId is not null)
            {
                headers.Add(KafkaMessageHeaders.LedgerGenerationId, Encoding.UTF8.GetBytes(ledgerGenerationId));
            }

            return headers;
        }

        private static KafkaOptions CreateKafkaOptions() => new()
        {
            ConsumerSettings = new ConsumerSettings
            {
                BootstrapServers = "127.0.0.1:1",
                ClientId = "account-generation-test",
                GroupId = "account-generation-test",
                Debug = null
            }
        };

        private sealed class RecordingAccountOperationsHandler : IAccountOperationsHandler
        {
            public int CallCount { get; private set; }

            public AccountOperationEvent Handled { get; private set; }

            public Task HandleAsync(AccountOperationEvent paymentEvent, CancellationToken cancellationToken)
            {
                CallCount++;
                Handled = paymentEvent;
                return Task.CompletedTask;
            }
        }
    }
}
