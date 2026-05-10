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
        public async Task SyncHistory_WhenOneOperationIsRemovedFromPeriod_ThenRewritesPeriodWithoutRemovedRecord()
        {
            var paymentAccountId = Guid.Parse("39e4f557-df45-4b61-9a02-a289de12136c");
            var removedOperationId = Guid.Parse("21ca49e6-369b-420f-90ca-dd10b7ea7f7a");
            var activeOperationId = Guid.Parse("fce777ae-4d3f-493c-b30b-d247a3477339");
            var categoryId = Guid.Parse("ca44071a-1bab-455a-acf1-a578a4ffafb2");
            var operationDay = new DateOnly(2024, 1, 12);

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = removedOperationId,
                        CategoryId = categoryId,
                        Amount = 12.10m,
                        OperationDay = operationDay
                    }
                },
                new()
                {
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = activeOperationId,
                        CategoryId = categoryId,
                        Amount = 5.25m,
                        OperationDay = operationDay.AddDays(1)
                    }
                },
                new()
                {
                    EventType = PaymentEventTypes.Removed,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = removedOperationId,
                        CategoryId = categoryId,
                        Amount = 12.10m,
                        OperationDay = operationDay
                    }
                }
            };

            var paymentsHistoryClientMock = new Mock<IPaymentsHistoryDocumentsClient>();
            IReadOnlyCollection<PaymentOperationHistoryRecord> rewrittenRecords = null;

            paymentsHistoryClientMock
                .Setup(c => c.RewriteAllAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<PaymentOperationHistoryRecord>>(),
                    It.IsAny<Guid>()))
                .Callback<string, IEnumerable<PaymentOperationHistoryRecord>, Guid>((_, records, _) => rewrittenRecords = records.ToList())
                .Returns(Task.CompletedTask);

            paymentsHistoryClientMock
                .Setup(c => c.BeginProjectionRunAsync(It.IsAny<ProjectionAuditRecord>()))
                .Returns(Task.CompletedTask);

            paymentsHistoryClientMock
                .Setup(c => c.CompleteProjectionRunAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var sut = new PaymentOperationsHistoryService(
                paymentsHistoryClientMock.Object,
                BuildCategoriesClientMock(categoryId).Object);
            var periodIdentifier = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(paymentAccountId);

            var result = await sut.SyncHistoryAsync(periodIdentifier, events);

            Assert.Multiple(() =>
            {
                result.Payload.Should().Be(5.25m);
                rewrittenRecords.Should().ContainSingle();
                rewrittenRecords.Single().Record.Key.Should().Be(activeOperationId);
                paymentsHistoryClientMock.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never);
            });
        }

        [Test]
        public async Task SyncHistory_WhenDuplicateOperationKey_ThenWritesSingleDeterministicLatestRecord()
        {
            var paymentAccountId = Guid.Parse("55c6f3fc-0db4-4012-9218-1764c558974d");
            var operationId = Guid.Parse("5b808c27-25d2-4a85-a0cb-947388fa1667");
            var categoryId = Guid.Parse("ca44071a-1bab-455a-acf1-a578a4ffafb2");
            var operationDay = new DateOnly(2024, 3, 12);
            var olderEnvelope = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var newerEnvelope = Guid.Parse("00000000-0000-0000-0000-000000000002");
            IReadOnlyCollection<PaymentOperationHistoryRecord> rewrittenRecords = null;

            var events = new List<PaymentOperationEvent>
            {
                new()
                {
                    EventType = PaymentEventTypes.Updated,
                    EnvelopId = newerEnvelope,
                    SequenceNumber = 2,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = categoryId,
                        Amount = 19m,
                        OperationDay = operationDay
                    }
                },
                new()
                {
                    EventType = PaymentEventTypes.Updated,
                    EnvelopId = olderEnvelope,
                    SequenceNumber = 1,
                    Payload = new FinancialTransaction
                    {
                        PaymentAccountId = paymentAccountId,
                        Key = operationId,
                        CategoryId = categoryId,
                        Amount = 10m,
                        OperationDay = operationDay
                    }
                }
            };

            var paymentsHistoryClientMock = new Mock<IPaymentsHistoryDocumentsClient>();
            paymentsHistoryClientMock
                .Setup(c => c.BeginProjectionRunAsync(It.IsAny<ProjectionAuditRecord>()))
                .Returns(Task.CompletedTask);
            paymentsHistoryClientMock
                .Setup(c => c.CompleteProjectionRunAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            paymentsHistoryClientMock
                .Setup(c => c.RewriteAllAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<PaymentOperationHistoryRecord>>(),
                    It.IsAny<Guid>()))
                .Callback<string, IEnumerable<PaymentOperationHistoryRecord>, Guid>((_, records, _) => rewrittenRecords = records.ToList())
                .Returns(Task.CompletedTask);

            var sut = new PaymentOperationsHistoryService(
                paymentsHistoryClientMock.Object,
                BuildCategoriesClientMock(categoryId).Object);

            var result = await sut.SyncHistoryAsync(
                operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(paymentAccountId),
                events);

            result.Payload.Should().Be(19m);
            rewrittenRecords.Should().ContainSingle();
            rewrittenRecords.Single().Record.Amount.Should().Be(19m);
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

            var categoriesClient = BuildCategoriesClientMock(Guid.Parse("ca44071a-1bab-455a-acf1-a578a4ffafb2"));

            paymentsHistoryClientMock
                .Setup(c => c.RewriteAllAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<PaymentOperationHistoryRecord>>(),
                    It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);

            paymentsHistoryClientMock
                .Setup(c => c.RemoveAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            paymentsHistoryClientMock
                .Setup(c => c.BeginProjectionRunAsync(It.IsAny<ProjectionAuditRecord>()))
                .Returns(Task.CompletedTask);

            paymentsHistoryClientMock
                .Setup(c => c.CompleteProjectionRunAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            return new PaymentOperationsHistoryService(
                paymentsHistoryClientMock.Object,
                categoriesClient.Object);
        }

        private static Mock<ICategoryDocumentsClient> BuildCategoriesClientMock(Guid categoryId)
        {
            var categoriesClient = new Mock<ICategoryDocumentsClient>();

            var category = new Category(
                CategoryTypes.Income,
                [
                    "test-category"
                ])
            {
                Key = categoryId
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

            return categoriesClient;
        }
    }
}
