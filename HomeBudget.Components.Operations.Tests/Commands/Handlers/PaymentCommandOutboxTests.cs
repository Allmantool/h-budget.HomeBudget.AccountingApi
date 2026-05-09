using System;
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
using HomeBudget.Components.Operations.MapperProfileConfigurations;
using HomeBudget.Components.Operations.Services.Interfaces;

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
