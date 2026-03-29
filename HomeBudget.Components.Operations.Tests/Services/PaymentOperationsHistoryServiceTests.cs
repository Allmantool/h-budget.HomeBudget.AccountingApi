using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Tests.Services
{
    [TestFixture]
    public class PaymentOperationsHistoryServiceTests
    {
        [Test]
        public async Task SyncHistoryAsync_WhenTryToAddOperationWithTheSameKey_ThenIgnoreTheDuplicateForSameAccount()
        {
            var paymentAccountId = Guid.Parse("db2d514d-f571-4876-936a-784f24fc3060");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var categoryId = Guid.Parse("ca44071a-1bab-455a-acf1-a578a4ffafb2");

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = categoryId,
                        Amount = 12.10m,
                    }
                },
                new()
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = categoryId,
                        Amount = 12.10m,
                    }
                }
            };

            var sut = BuildServiceUnderTest();
            var periodIdentifier = new DateOnly(2024, 12, 27).ToFinancialPeriod().ToFinancialMonthIdentifier(paymentAccountId);
            var result = await sut.SyncHistoryAsync(periodIdentifier, events);

            result.Payload.Should().Be(12.10m);
        }

        [Test]
        public async Task SyncHistory_WhenUpdateSeveralTimes_ThenTheMostUpToDateDataShouldBeApplied()
        {
            var paymentAccountId = Guid.Parse("36ab7a9c-66ac-4b6e-8765-469572daa46b");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var categoryId = Guid.Parse("ca44071a-1bab-455a-acf1-a578a4ffafb2");

            var operationDay = new DateOnly(2023, 12, 15);

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = categoryId,
                        Amount = 12.10m,
                        OperationDay = operationDay
                    }
                },
                new()
                {
                    EventType = PaymentEventTypes.Updated,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = categoryId,
                        Amount = 17.12m,
                        OperationDay = operationDay
                    }
                },
                new()
                {
                    EventType = PaymentEventTypes.Updated,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = categoryId,
                        Amount = 98.98m,
                        OperationDay = operationDay
                    }
                }
            };

            var sut = BuildServiceUnderTest();
            var periodIdentifier = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(paymentAccountId);
            var result = await sut.SyncHistoryAsync(periodIdentifier, events);

            result.Payload.Should().Be(98.98m);
        }

        [Test]
        public async Task SyncHistory_WhenRemoveNotExisted_ThenSkipForCalculation()
        {
            var paymentAccountId = Guid.Parse("1f34a993-8279-4f61-b86b-658c0d3703d7");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = PaymentEventTypes.Removed,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId
                    }
                }
            };

            var sut = BuildServiceUnderTest();
            var periodIdentifier = new DateOnly(2024, 12, 27).ToFinancialPeriod().ToFinancialMonthIdentifier(paymentAccountId);
            var result = await sut.SyncHistoryAsync(periodIdentifier, events);

            result.Payload.Should().Be(0);
        }

        [Test]
        public async Task SyncHistory_WhenRemoveExisted_ThenShouldBeRemoved()
        {
            var paymentAccountId = Guid.Parse("d6971a06-5ab1-48f1-a6f6-1cd0cd1df220");
            var operationId = Guid.Parse("b275b2bc-e159-4eb3-a85a-22728c4cb037");

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        Amount = 12.10m,
                    }
                },
                new()
                {
                    EventType = PaymentEventTypes.Removed,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId
                    }
                }
            };

            var sut = BuildServiceUnderTest();
            var periodIdentifier = new DateOnly(2024, 12, 27).ToFinancialPeriod().ToFinancialMonthIdentifier(paymentAccountId);
            var result = await sut.SyncHistoryAsync(periodIdentifier, events);

            result.Payload.Should().Be(0);
        }

        [Test]
        public async Task SyncHistoryAsync_WhenDeletingOneOfThreeOperations_RewritesHistoryWithRemainingRecordsOnly()
        {
            var paymentAccountId = Guid.Parse("70a4fb0b-80db-4911-a554-e5883f33867a");
            var categoryId = Guid.Parse("ca44071a-1bab-455a-acf1-a578a4ffafb2");
            var firstOperationId = Guid.Parse("ac0ba251-65b1-4ef7-b558-b9250ca4f7d8");
            var secondOperationId = Guid.Parse("bb8a3ed6-2aa4-4b4c-8571-55c2ba0def7e");
            var thirdOperationId = Guid.Parse("0a6eb127-b238-4381-a0bb-f652e8d0d57e");

            var events = new List<PaymentOperationEvent>
            {
                CreateEvent(PaymentEventTypes.Added, paymentAccountId, firstOperationId, categoryId, 10m, new DateOnly(2024, 1, 5)),
                CreateEvent(PaymentEventTypes.Added, paymentAccountId, secondOperationId, categoryId, 20m, new DateOnly(2024, 1, 6)),
                CreateEvent(PaymentEventTypes.Added, paymentAccountId, thirdOperationId, categoryId, 30m, new DateOnly(2024, 1, 7)),
                CreateEvent(PaymentEventTypes.Removed, paymentAccountId, secondOperationId, categoryId, 20m, new DateOnly(2024, 1, 6)),
            };

            var paymentsHistoryClientMock = new Mock<IPaymentsHistoryDocumentsClient>();
            var sut = BuildServiceUnderTest(paymentsHistoryClientMock);
            var periodIdentifier = new DateOnly(2024, 1, 5).ToFinancialPeriod().ToFinancialMonthIdentifier(paymentAccountId);

            var result = await sut.SyncHistoryAsync(periodIdentifier, events);

            result.Payload.Should().Be(40m);

            paymentsHistoryClientMock.Verify(
                client => client.RewriteAllAsync(
                    periodIdentifier,
                    It.Is<IEnumerable<PaymentOperationHistoryRecord>>(records =>
                        records.Select(record => record.Record.Key).SequenceEqual(new[] { firstOperationId, thirdOperationId }) &&
                        records.Select(record => record.Balance).SequenceEqual(new[] { 10m, 40m }))),
                Times.Once);
            paymentsHistoryClientMock.Verify(
                client => client.BulkWriteAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PaymentOperationHistoryRecord>>()),
                Times.Never);
            paymentsHistoryClientMock.Verify(
                client => client.ReplaceOneAsync(It.IsAny<string>(), It.IsAny<PaymentOperationHistoryRecord>()),
                Times.Never);
        }

        private static PaymentOperationsHistoryService BuildServiceUnderTest(Mock<IPaymentsHistoryDocumentsClient> paymentsHistoryClientMock = null)
        {
            paymentsHistoryClientMock ??= new Mock<IPaymentsHistoryDocumentsClient>();
            var paymentAccountDocumentClientMock = new Mock<IPaymentAccountDocumentClient>();

            paymentAccountDocumentClientMock
                .Setup(cl => cl.GetByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(new PaymentAccountDocument
                {
                    Payload = new PaymentAccount
                    {
                        InitialBalance = 0
                    }
                }));

            var categoriesClient = new Mock<ICategoryDocumentsClient>();

            var category = new Category(
                CategoryTypes.Income,
                [
                    "test-category"
                ])
            {
                Key = Guid.Parse("ca44071a-1bab-455a-acf1-a578a4ffafb2")
            };

            var payload = new CategoryDocument
            {
                Payload = category
            };

            categoriesClient
                .Setup(c => c.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(() => Result<CategoryDocument>.Succeeded(payload));

            categoriesClient
                .Setup(c => c.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(() => Result<IReadOnlyCollection<CategoryDocument>>.Succeeded([payload]));

            return new PaymentOperationsHistoryService(
                paymentsHistoryClientMock.Object,
                categoriesClient.Object);
        }

        private static PaymentOperationEvent CreateEvent(
            PaymentEventTypes eventType,
            Guid paymentAccountId,
            Guid operationId,
            Guid categoryId,
            decimal amount,
            DateOnly operationDay)
        {
            return new PaymentOperationEvent
            {
                EventType = eventType,
                Payload = new FinancialTransaction
                {
                    PaymentAccountId = paymentAccountId,
                    Key = operationId,
                    CategoryId = categoryId,
                    Amount = amount,
                    OperationDay = operationDay
                }
            };
        }
    }
}
