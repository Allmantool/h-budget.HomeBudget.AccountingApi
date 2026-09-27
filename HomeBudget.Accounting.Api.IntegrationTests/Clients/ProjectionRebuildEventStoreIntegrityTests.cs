using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Accounting.Workers.OperationsConsumer.Clients;
using HomeBudget.Components.Categories.Clients;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [Order(IntegrationTestOrderIndex.PaymentOperationsEventStoreClientTests)]
    public sealed class ProjectionRebuildEventStoreIntegrityTests : BaseIntegrationTests
    {
        [Test]
        public async Task RebuildFromCompleteStream_RepairsProjectionWithoutChangingAuthoritativeEvents()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var mongoOptions = Options.Create(new MongoDbOptions
            {
                ConnectionString = TestContainers.MongoDbContainer.GetConnectionString(),
                PaymentsHistory = $"rebuild_integrity_history_{suffix}",
                HandBooks = $"rebuild_integrity_handbooks_{suffix}"
            });
            using var eventStoreClient = new EventStoreClient(EventStoreClientSettings.Create(
                TestContainers.EventSourceDbContainer.GetConnectionString()));
            var eventStoreOptions = Options.Create(new EventStoreDbOptions
            {
                RetryAttempts = 3,
                TimeoutInSeconds = 10
            });
            using var writer = new PaymentOperationsEventStoreWriteClient(
                NullLogger<PaymentOperationsEventStoreWriteClient>.Instance,
                eventStoreClient,
                eventStoreOptions);
            using var reader = new PaymentOperationsEventStoreStreamReadClient(
                NullLogger<PaymentOperationsEventStoreStreamReadClient>.Instance,
                Mock.Of<IServiceScopeFactory>(),
                Mock.Of<IDateTimeProvider>(),
                eventStoreClient,
                eventStoreOptions);
            using var history = new PaymentsHistoryDocumentsClient(mongoOptions);
            using var categories = new CategoryDocumentsClient(mongoOptions);
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var operationDay = new DateOnly(2026, 3, 1);
            var period = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(accountId);
            var streamId = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(period);
            await categories.InsertOneAsync(new Category(CategoryTypes.Income, ["rebuild-integrity"])
            {
                Key = categoryId
            });
            var events = Enumerable.Range(0, 3)
                .Select(index => new PaymentOperationEvent
                {
                    EnvelopId = Guid.NewGuid(),
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        OperationUnixTime = 1772323200000L + index,
                        PaymentAccountId = accountId,
                        CategoryId = categoryId,
                        ContractorId = Guid.NewGuid(),
                        TransactionType = TransactionTypes.Payment,
                        ConversionMultiplier = 1.25m,
                        ScopedOperationId = index + 1,
                        OperationDay = operationDay.AddDays(index),
                        Amount = 10m + index,
                        Comment = $"rebuild-integrity-{index}"
                    }
                })
                .ToArray();
            await writer.SendBatchAsync(events, streamId);
            var beforeEvents = await reader.ReadAsync(streamId).ToListAsync();
            foreach (var paymentEvent in beforeEvents.Take(2))
            {
                await history.ReplaceOneAsync(period, new PaymentOperationHistoryRecord
                {
                    StreamRevision = paymentEvent.SequenceNumber,
                    Balance = paymentEvent.Payload.Amount,
                    Record = paymentEvent.Payload
                });
            }

            var damaged = await history.GetAsync(accountId, operationDay.ToFinancialPeriod());
            var service = new PaymentOperationsHistoryService(history, categories);
            await service.SyncHistoryAsync(
                period,
                beforeEvents,
                new ProjectionCheckpoint
                {
                    StreamId = streamId,
                    Revision = beforeEvents[^1].SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            var afterEvents = await reader.ReadAsync(streamId).ToListAsync();
            var repaired = await history.GetAsync(accountId, operationDay.ToFinancialPeriod());

            damaged.Should().HaveCount(2);
            repaired.Should().HaveCount(3);
            EventEvidence(afterEvents).Should().Equal(EventEvidence(beforeEvents));
        }

        private static object[] EventEvidence(IEnumerable<PaymentOperationEvent> events)
        {
            return events.Select(paymentEvent => (object)new
            {
                paymentEvent.SequenceNumber,
                paymentEvent.EnvelopId,
                paymentEvent.EventType,
                paymentEvent.Payload.Key,
                paymentEvent.Payload.PaymentAccountId,
                paymentEvent.Payload.CategoryId,
                paymentEvent.Payload.Amount,
                paymentEvent.Payload.OperationDay,
                paymentEvent.Payload.OperationUnixTime,
                paymentEvent.Payload.Comment,
                PayloadHash = Convert.ToHexString(SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(paymentEvent.Payload)))
            }).ToArray();
        }
    }
}
