using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Commands.Handlers;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.MapperProfileConfigurations;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Constants;

namespace HomeBudget.Components.Operations.Tests.Commands.Handlers
{
    [TestFixture]
    public class PaymentCommandOutboxTests
    {
        [Test]
        public async Task Handle_WhenOutboxInsertFails_ThenCommandFails()
        {
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox
                .Setup(x => x.WriteRecordAsync(It.IsAny<OutboxAccountPaymentsEntity>()))
                .ThrowsAsync(new InvalidOperationException("sql insert failed"));

            var handler = new AddPaymentOperationCommandHandler(
                BuildMapper(),
                BuildDateTimeProvider().Object,
                outbox.Object);
            var command = new AddPaymentOperationCommand(new FinancialTransaction
            {
                Key = Guid.NewGuid(),
                PaymentAccountId = Guid.NewGuid(),
                OperationDay = new DateOnly(2026, 05, 09),
                Amount = 25m
            })
            {
                CorrelationId = Guid.NewGuid().ToString()
            };

            Func<Task> act = () => handler.Handle(command, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("sql insert failed");
            outbox.Verify(
                x => x.WriteRecordAsync(It.Is<OutboxAccountPaymentsEntity>(
                    row => row.AggregateId == command.OperationForAdd.PaymentAccountId.ToString() &&
                           row.OperationId == command.OperationForAdd.Key.ToString() &&
                           row.Payload.Contains(command.OperationForAdd.Key.ToString()))),
                Times.Once);
        }

        [Test]
        public async Task Handle_WhenCommandIsWrittenToOutbox_ThenAddsIdempotencyMetadata()
        {
            OutboxAccountPaymentsEntity capturedRow = null;
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox
                .Setup(x => x.WriteRecordAsync(It.IsAny<OutboxAccountPaymentsEntity>()))
                .Callback<OutboxAccountPaymentsEntity>(row => capturedRow = row)
                .Returns(Task.CompletedTask);

            var handler = new AddPaymentOperationCommandHandler(
                BuildMapper(),
                BuildDateTimeProvider().Object,
                outbox.Object);
            var command = new AddPaymentOperationCommand(new FinancialTransaction
            {
                Key = Guid.NewGuid(),
                PaymentAccountId = Guid.NewGuid(),
                OperationDay = new DateOnly(2026, 05, 09),
                Amount = 25m
            })
            {
                CorrelationId = Guid.NewGuid().ToString()
            };

            await handler.Handle(command, CancellationToken.None);

            using var payload = JsonDocument.Parse(capturedRow!.Payload);
            var metadata = payload.RootElement.GetProperty(nameof(PaymentOperationEvent.Metadata));
            metadata.GetProperty(EventMetadataKeys.MessageId).GetString().Should().Be(capturedRow.MessageId);
            metadata.GetProperty(EventMetadataKeys.CommandId).GetString().Should().Be(capturedRow.MessageId);
            metadata.GetProperty(EventMetadataKeys.CorrelationId).GetString().Should().Be(command.CorrelationId);
            metadata.GetProperty(EventMetadataKeys.SourceSystem).GetString().Should().Be(PaymentOperationEventIdentity.DefaultSourceSystem);
            metadata.GetProperty("ledger-id").GetString().Should().Be("default");
            metadata.GetProperty("ledger-generation-id").GetString().Should().Be("legacy");
        }

        [Test]
        public async Task Handle_WhenActiveGenerationChanges_ThenPersistsServerSelectedScope()
        {
            OutboxAccountPaymentsEntity capturedRow = null;
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox
                .Setup(service => service.WriteRecordAsync(It.IsAny<OutboxAccountPaymentsEntity>()))
                .Callback<OutboxAccountPaymentsEntity>(row => capturedRow = row)
                .Returns(Task.CompletedTask);
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver
                .Setup(service => service.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerGenerationScope("default", "replacement-2", 2, "\"token-2\""));
            var handler = new AddPaymentOperationCommandHandler(
                BuildMapper(),
                BuildDateTimeProvider().Object,
                outbox.Object,
                resolver.Object);
            var command = new AddPaymentOperationCommand(new FinancialTransaction
            {
                Key = Guid.NewGuid(),
                PaymentAccountId = Guid.NewGuid(),
                OperationDay = new DateOnly(2026, 05, 09),
                Amount = 25m
            })
            {
                CorrelationId = Guid.NewGuid().ToString()
            };

            await handler.Handle(command, CancellationToken.None);

            capturedRow.Should().NotBeNull();
            capturedRow!.LedgerId.Should().Be("default");
            capturedRow.LedgerGenerationId.Should().Be("replacement-2");
            using var payload = JsonDocument.Parse(capturedRow.Payload);
            var metadata = payload.RootElement.GetProperty(nameof(PaymentOperationEvent.Metadata));
            metadata.GetProperty(EventMetadataKeys.LedgerGenerationId).GetString().Should().Be("replacement-2");
        }

        [Test]
        public async Task Handle_WhenAdmissionAlreadyResolvedGeneration_DoesNotResolveOrStampALaterGeneration()
        {
            var admittedGeneration = new LedgerGenerationScope("default", "generation-1", 1, "\"token-1\"");
            OutboxAccountPaymentsEntity capturedRow = null;
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox
                .Setup(service => service.WriteRecordAsync(It.IsAny<OutboxAccountPaymentsEntity>()))
                .Callback<OutboxAccountPaymentsEntity>(row => capturedRow = row)
                .Returns(Task.CompletedTask);
            var resolver = new Mock<IActiveLedgerGenerationResolver>();
            resolver
                .Setup(service => service.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerGenerationScope("default", "generation-2", 2, "\"token-2\""));
            var handler = new AddPaymentOperationCommandHandler(
                BuildMapper(),
                BuildDateTimeProvider().Object,
                outbox.Object,
                resolver.Object);
            var command = new AddPaymentOperationCommand(new FinancialTransaction
            {
                Key = Guid.NewGuid(),
                PaymentAccountId = Guid.NewGuid(),
                OperationDay = new DateOnly(2026, 05, 09),
                Amount = 25m
            })
            {
                CorrelationId = Guid.NewGuid().ToString(),
                ExpectedGeneration = admittedGeneration
            };

            await handler.Handle(command, CancellationToken.None);

            capturedRow.Should().NotBeNull();
            capturedRow!.LedgerGenerationId.Should().Be("generation-1");
            capturedRow.GenerationTokenValue.Should().Be("token-1");
            resolver.Verify(
                service => service.GetActiveAsync(It.IsAny<CancellationToken>()),
                Times.Never,
                "the HTTP admission generation must remain immutable through durable acceptance");
        }

        [Test]
        public async Task ApplyTransfer_WhenGenerationSwitchCanRaceAcceptance_WritesBothSidesAsOneGuardedBatch()
        {
            var admittedGeneration = new LedgerGenerationScope("default", "generation-1", 1, "\"token-1\"");
            OutboxAccountPaymentsEntity senderRow = null;
            OutboxAccountPaymentsEntity recipientRow = null;
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox
                .Setup(service => service.WriteTransferRecordsAsync(
                    It.IsAny<OutboxAccountPaymentsEntity>(),
                    It.IsAny<OutboxAccountPaymentsEntity>()))
                .Callback<OutboxAccountPaymentsEntity, OutboxAccountPaymentsEntity>((sender, recipient) =>
                {
                    senderRow = sender;
                    recipientRow = recipient;
                })
                .Returns(Task.CompletedTask);
            var transfer = new CrossAccountsTransferOperation
            {
                PaymentOperations =
                [
                    new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        PaymentAccountId = Guid.NewGuid(),
                        OperationDay = new DateOnly(2026, 05, 09),
                        Amount = -25m
                    },

                    new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        PaymentAccountId = Guid.NewGuid(),
                        OperationDay = new DateOnly(2026, 05, 09),
                        Amount = 25m
                    }

                ]
            };
            var handler = new ApplyTransferCommandHandler(
                BuildMapper(),
                BuildDateTimeProvider().Object,
                outbox.Object);
            var command = new ApplyTransferCommand(transfer)
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                ExpectedGeneration = admittedGeneration
            };

            await handler.Handle(command, CancellationToken.None);

            Assert.Multiple(() =>
            {
                senderRow.Should().NotBeNull();
                recipientRow.Should().NotBeNull();
                senderRow!.LedgerGenerationId.Should().Be("generation-1");
                recipientRow!.LedgerGenerationId.Should().Be("generation-1");
                senderRow.GenerationTokenValue.Should().Be("token-1");
                recipientRow.GenerationTokenValue.Should().Be("token-1");
            });
            outbox.Verify(service => service.WriteTransferRecordsAsync(senderRow, recipientRow), Times.Once);
            outbox.Verify(
                service => service.WriteRecordAsync(It.IsAny<OutboxAccountPaymentsEntity>()),
                Times.Never,
                "separate child commits could leave only one transfer side accepted");
        }

        [Test]
        public async Task UpdateAndRemoveTransfer_WhenAccepted_WriteEachCanonicalPairAsOneGuardedBatch()
        {
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox
                .Setup(service => service.WriteTransferRecordsAsync(
                    It.IsAny<OutboxAccountPaymentsEntity>(),
                    It.IsAny<OutboxAccountPaymentsEntity>()))
                .Returns(Task.CompletedTask);
            var transfer = new CrossAccountsTransferOperation
            {
                PaymentOperations =
                [
                    new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        PaymentAccountId = Guid.NewGuid(),
                        OperationDay = new DateOnly(2026, 05, 09),
                        Amount = -25m
                    },

                    new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        PaymentAccountId = Guid.NewGuid(),
                        OperationDay = new DateOnly(2026, 05, 09),
                        Amount = 25m
                    }

                ]
            };
            var updateHandler = new UpdateTransferCommandHandler(
                BuildMapper(),
                BuildDateTimeProvider().Object,
                outbox.Object);
            var removeHandler = new RemoveTransferCommandHandler(
                BuildMapper(),
                BuildDateTimeProvider().Object,
                outbox.Object);

            await updateHandler.Handle(
                new UpdateTransferCommand(transfer) { ExpectedGeneration = LedgerGenerationScope.Legacy },
                CancellationToken.None);
            await removeHandler.Handle(
                new RemoveTransferCommand(transfer) { ExpectedGeneration = LedgerGenerationScope.Legacy },
                CancellationToken.None);

            outbox.Verify(
                service => service.WriteTransferRecordsAsync(
                    It.IsAny<OutboxAccountPaymentsEntity>(),
                    It.IsAny<OutboxAccountPaymentsEntity>()),
                Times.Exactly(2));
            outbox.Verify(
                service => service.WriteRecordAsync(It.IsAny<OutboxAccountPaymentsEntity>()),
                Times.Never);
        }

        [Test]
        public async Task Handle_WhenIdempotentCommandIsRepeated_ThenReturnsTheOriginalOperationIdWithoutAnotherRecord()
        {
            var originalOperationId = Guid.NewGuid();
            var commandId = Guid.NewGuid().ToString();
            var outbox = new Mock<IOutboxPaymentStatusService>();
            outbox
                .Setup(x => x.WriteIdempotentRecordAsync(It.IsAny<OutboxAccountPaymentsEntity>()))
                .ReturnsAsync(new PaymentCommandRegistration
                {
                    CommandId = commandId,
                    OperationId = originalOperationId,
                    WasAlreadyAccepted = true,
                    RequestFingerprint = "fingerprint"
                });

            var commandContext = new PaymentCommandContext
            {
                IdempotencyKeyHash = "key-hash",
                RequestFingerprint = "fingerprint",
                CommandType = PaymentCommandTypes.Create
            };
            var handler = new AddPaymentOperationCommandHandler(BuildMapper(), BuildDateTimeProvider().Object, outbox.Object);
            var command = new AddPaymentOperationCommand(new FinancialTransaction
            {
                Key = Guid.NewGuid(),
                PaymentAccountId = Guid.NewGuid(),
                OperationDay = new DateOnly(2026, 05, 09),
                Amount = 25m
            })
            {
                CorrelationId = Guid.NewGuid().ToString(),
                CommandContext = commandContext
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeTrue();
                result.Payload.Should().Be(originalOperationId);
                commandContext.CommandId.Should().Be(commandId);
                commandContext.WasAlreadyAccepted.Should().BeTrue();
            });
            outbox.Verify(x => x.WriteRecordAsync(It.IsAny<OutboxAccountPaymentsEntity>()), Times.Never);
        }

        private static IMapper BuildMapper()
        {
            var configurationExpression = new MapperConfigurationExpression();
            configurationExpression.AddProfile<PaymentOperationEventMappingProfile>();

            return new MapperConfiguration(configurationExpression, NullLoggerFactory.Instance).CreateMapper();
        }

        private static Mock<IDateTimeProvider> BuildDateTimeProvider()
        {
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider
                .Setup(x => x.GetNowUtc())
                .Returns(new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc));

            return dateTimeProvider;
        }
    }
}
