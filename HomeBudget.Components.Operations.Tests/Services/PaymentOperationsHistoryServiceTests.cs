using System;
using System.Collections.Generic;
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

        private static PaymentOperationsHistoryService BuildServiceUnderTest()
        {
            var paymentsHistoryClientMock = new Mock<IPaymentsHistoryDocumentsClient>();
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
    }
}