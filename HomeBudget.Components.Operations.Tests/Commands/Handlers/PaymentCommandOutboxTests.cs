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
