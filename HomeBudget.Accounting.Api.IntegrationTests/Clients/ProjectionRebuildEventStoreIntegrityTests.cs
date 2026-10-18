using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;
using Microsoft.Data.SqlClient;
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
using HomeBudget.Components.Accounts.Clients;
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
                HandBooks = $"rebuild_integrity_handbooks_{suffix}",
                LedgerDatabase = $"rebuild_integrity_ledger_{suffix}",
                PaymentAccounts = $"rebuild_integrity_accounts_{suffix}"
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
            using var accounts = new PaymentAccountDocumentClient(mongoOptions);
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var operationDay = new DateOnly(2026, 3, 1);
            var period = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(accountId);
            var streamId = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(period);
            await categories.InsertOneAsync(new Category(CategoryTypes.Income, ["rebuild-integrity"])
            {
                Key = categoryId
            });
            await accounts.InsertOneAsync(new PaymentAccount
            {
                Key = accountId,
                Type = AccountTypes.Cash,
                Currency = "BYN",
                Balance = 125m,
                InitialBalance = 100m,
                Agent = "source-bank",
                Description = "authoritative settings sentinel"
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
            var categoryBefore = Serialize(await categories.GetByIdAsync(categoryId));
            var accountBefore = Serialize(await accounts.GetByIdAsync(accountId.ToString()));
            var sqlBefore = await ReadSqlSourceFingerprintsAsync();
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
            var categoryAfter = Serialize(await categories.GetByIdAsync(categoryId));
            var accountAfter = Serialize(await accounts.GetByIdAsync(accountId.ToString()));
            var sqlAfter = await ReadSqlSourceFingerprintsAsync();

            damaged.Should().HaveCount(2);
            repaired.Should().HaveCount(3);
            EventEvidence(afterEvents).Should().Equal(EventEvidence(beforeEvents));
            categoryAfter.Should().Be(categoryBefore);
            accountAfter.Should().Be(accountBefore);
            sqlAfter.Should().BeEquivalentTo(sqlBefore);
        }

        private static string Serialize<T>(HomeBudget.Core.Models.Result<T> result)
        {
            result.IsSucceeded.Should().BeTrue(result.StatusMessage);
            return JsonSerializer.Serialize(result.Payload);
        }

        private async Task<IReadOnlyDictionary<string, SqlSourceFingerprint>> ReadSqlSourceFingerprintsAsync()
        {
            string[] accountingTables =
            [
                "OutboxAccountPayments",
                "PaymentInboxMessages",
                "TransferCommands"
            ];
            string[] controlTables =
            [
                "DataProtectionOperations",
                "DataProtectionPlans",
                "ProtectedAuditEntries"
            ];
            var result = new Dictionary<string, SqlSourceFingerprint>(StringComparer.Ordinal);
            await AddSqlFingerprintsAsync(
                result,
                TestContainers.AccountingDbConnectionString,
                accountingTables);
            var controlConnectionString = new SqlConnectionStringBuilder(TestContainers.AccountingDbConnectionString)
            {
                InitialCatalog = "HomeBudget.DataProtection"
            }.ConnectionString;
            await AddSqlFingerprintsAsync(result, controlConnectionString, controlTables);
            return result;
        }

        [SuppressMessage(
            "Security",
            "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "Table identifiers are selected only from the two fixed allowlists in this disposable integration test.")]
        private static async Task AddSqlFingerprintsAsync(
            IDictionary<string, SqlSourceFingerprint> result,
            string connectionString,
            IEnumerable<string> tables)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            foreach (var table in tables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.[{table}]";
                await using var reader = await command.ExecuteReaderAsync();
                await reader.ReadAsync();
                result.Add(
                    table,
                    new SqlSourceFingerprint(
                        reader.GetInt64(0),
                        reader.IsDBNull(1) ? null : reader.GetInt32(1)));
            }
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

        private sealed record SqlSourceFingerprint(long RowCount, int? Checksum);
    }
}
