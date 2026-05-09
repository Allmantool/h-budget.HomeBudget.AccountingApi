using System;
using System.Data;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Services;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public class OutboxPaymentStatusServiceTests
    {
        [Test]
        public async Task WriteRecordAsync_WhenSqlInsertFails_ThenExceptionBubbles()
        {
            var dependencies = BuildDependencies();
            dependencies.WriteRepository
                .Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<OutboxAccountPaymentsEntity>(),
                    It.IsAny<IDbTransaction>()))
                .ThrowsAsync(new InvalidOperationException("sql insert failed"));
            var sut = dependencies.BuildService();

            Func<Task> act = () => sut.WriteRecordAsync(BuildOutboxRow());

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("sql insert failed");
        }

        [Test]
        public async Task WriteRecordAsync_WhenMessageIdAlreadyExists_ThenSqlGuardsAgainstDuplicateInsert()
        {
            var dependencies = BuildDependencies();
            string capturedSql = null;
            dependencies.WriteRepository
                .Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<OutboxAccountPaymentsEntity>(),
                    It.IsAny<IDbTransaction>()))
                .Callback<string, OutboxAccountPaymentsEntity, IDbTransaction>((sql, _, _) => capturedSql = sql)
                .ReturnsAsync(0);
            var sut = dependencies.BuildService();

            await sut.WriteRecordAsync(BuildOutboxRow());

            capturedSql.Should().Contain("IF NOT EXISTS");
            capturedSql.Should().Contain("WHERE MessageId = @MessageId");
            dependencies.WriteRepository.Verify(
                x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<OutboxAccountPaymentsEntity>(),
                    It.IsAny<IDbTransaction>()),
                Times.Once);
        }

        [Test]
        public async Task MarkFailedAsync_WhenRetryLimitIsReached_ThenSqlMovesRowToDeadLetter()
        {
            var dependencies = BuildDependencies();
            string capturedSql = null;
            OutboxFailureUpdateEntity capturedParameters = null;
            dependencies.WriteRepository
                .Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<OutboxFailureUpdateEntity>(),
                    It.IsAny<IDbTransaction>()))
                .Callback<string, OutboxFailureUpdateEntity, IDbTransaction>((sql, parameters, _) =>
                {
                    capturedSql = sql;
                    capturedParameters = parameters;
                })
                .ReturnsAsync(1);
            var sut = dependencies.BuildService();

            await sut.MarkFailedAsync(
                Guid.NewGuid().ToString(),
                "publisher-1",
                "kafka down",
                3,
                new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc));

            capturedSql.Should().Contain("RetryCount = RetryCount + 1");
            capturedSql.Should().Contain("THEN @DeadLetterStatus");
            capturedParameters.LastError.Should().Be("kafka down");
            capturedParameters.MaxRetryAttempts.Should().Be(3);
        }

        private static OutboxStatusServiceDependencies BuildDependencies()
        {
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider
                .Setup(x => x.GetNowUtc())
                .Returns(new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc));

            return new OutboxStatusServiceDependencies(
                new Mock<IBaseWriteRepository>(),
                new Mock<IBaseReadRepository>(),
                dateTimeProvider);
        }

        private static OutboxAccountPaymentsEntity BuildOutboxRow()
        {
            var nowUtc = new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc);

            return new OutboxAccountPaymentsEntity
            {
                AggregateId = Guid.NewGuid().ToString(),
                OperationId = Guid.NewGuid().ToString(),
                EventType = "Added",
                MessageId = Guid.NewGuid().ToString(),
                PartitionKey = Guid.NewGuid().ToString(),
                Payload = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };
        }

        private sealed class OutboxStatusServiceDependencies(
            Mock<IBaseWriteRepository> writeRepository,
            Mock<IBaseReadRepository> readRepository,
            Mock<IDateTimeProvider> dateTimeProvider)
        {
            public Mock<IBaseWriteRepository> WriteRepository { get; } = writeRepository;

            public OutboxPaymentStatusService BuildService()
            {
                return new OutboxPaymentStatusService(
                    Mock.Of<ILogger<OutboxPaymentStatusService>>(),
                    dateTimeProvider.Object,
                    WriteRepository.Object,
                    readRepository.Object);
            }
        }
    }
}
