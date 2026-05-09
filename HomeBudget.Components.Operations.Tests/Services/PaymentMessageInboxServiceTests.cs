using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Infrastructure.Data.DbEntries;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Components.Operations.Services;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public class PaymentMessageInboxServiceTests
    {
        [Test]
        public async Task StartProcessingAsync_WhenMessageIsReplayRequested_ThenSqlAllowsProcessingAgain()
        {
            var dependencies = BuildDependencies();
            string capturedSql = null;
            dependencies.ReadRepository
                .Setup(x => x.SingleAsync<PaymentInboxStartResult>(
                    It.IsAny<string>(),
                    It.IsAny<object>()))
                .Callback<string, object>((sql, _) => capturedSql = sql)
                .ReturnsAsync(new PaymentInboxStartResult
                {
                    MessageId = "message-42",
                    Status = PaymentInboxStatus.Processing,
                    RetryCount = 0,
                    ShouldProcess = true
                });
            var sut = dependencies.BuildService();

            var result = await sut.StartProcessingAsync(BuildInboxRow());

            result.ShouldProcess.Should().BeTrue();
            capturedSql.Should().Contain("Status = 'ReplayRequested'");
            capturedSql.Should().Contain("Status NOT IN ('Processed', 'Poison')");
        }

        [Test]
        public async Task MarkFailedAsync_WhenRetryLimitIsReached_ThenReturnsPoisonState()
        {
            var dependencies = BuildDependencies();
            string capturedSql = null;
            dependencies.ReadRepository
                .Setup(x => x.GetAsync<PaymentInboxFailureResult>(
                    It.IsAny<string>(),
                    It.IsAny<object>()))
                .Callback<string, object>((sql, _) => capturedSql = sql)
                .ReturnsAsync(new List<PaymentInboxFailureResult>
                {
                    new()
                    {
                        MessageId = "message-42",
                        Status = PaymentInboxStatus.Poison,
                        RetryCount = 5
                    }
                });
            var sut = dependencies.BuildService();

            var result = await sut.MarkFailedAsync(
                "message-42",
                "eventstore down",
                5,
                new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc));

            result.IsPoison.Should().BeTrue();
            capturedSql.Should().Contain("RetryCount = RetryCount + 1");
            capturedSql.Should().Contain("THEN 'Poison'");
        }

        [Test]
        public async Task RequestReplayAsync_WhenMessageIsPoison_ThenMovesMessageToReplayRequested()
        {
            var dependencies = BuildDependencies();
            string capturedSql = null;
            PaymentInboxMessageUpdateEntity capturedParameters = null;
            dependencies.WriteRepository
                .Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<PaymentInboxMessageUpdateEntity>(),
                    It.IsAny<IDbTransaction>()))
                .Callback<string, PaymentInboxMessageUpdateEntity, IDbTransaction>((sql, parameters, _) =>
                {
                    capturedSql = sql;
                    capturedParameters = parameters;
                })
                .ReturnsAsync(1);
            var sut = dependencies.BuildService();

            await sut.RequestReplayAsync(
                "message-42",
                new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc));

            capturedSql.Should().Contain("AND Status = 'Poison'");
            capturedSql.Should().Contain("ProcessedUtc = NULL");
            capturedParameters.Status.Should().Be(PaymentInboxStatus.ReplayRequested);
        }

        private static PaymentInboxMessageEntity BuildInboxRow()
        {
            var nowUtc = new DateTime(2026, 05, 09, 12, 0, 0, DateTimeKind.Utc);

            return new PaymentInboxMessageEntity
            {
                MessageId = "message-42",
                Topic = "accounting.payments",
                Partition = 2,
                Offset = 10,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc,
                RawMessage = "{}"
            };
        }

        private static PaymentInboxServiceDependencies BuildDependencies()
        {
            return new PaymentInboxServiceDependencies(
                new Mock<IBaseReadRepository>(),
                new Mock<IBaseWriteRepository>());
        }

        private sealed class PaymentInboxServiceDependencies(
            Mock<IBaseReadRepository> readRepository,
            Mock<IBaseWriteRepository> writeRepository)
        {
            public Mock<IBaseReadRepository> ReadRepository { get; } = readRepository;

            public Mock<IBaseWriteRepository> WriteRepository { get; } = writeRepository;

            public PaymentMessageInboxService BuildService()
            {
                return new PaymentMessageInboxService(
                    Mock.Of<ILogger<PaymentMessageInboxService>>(),
                    ReadRepository.Object,
                    WriteRepository.Object);
            }
        }
    }
}
