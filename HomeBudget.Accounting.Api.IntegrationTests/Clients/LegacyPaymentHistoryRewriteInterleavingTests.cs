using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using MongoDB.Driver;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [Order(IntegrationTestOrderIndex.MongoDocumentUniquenessTests)]
    public sealed class LegacyPaymentHistoryRewriteInterleavingTests : BaseIntegrationTests
    {
        private IMongoDatabase _database;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            var client = new MongoClient(TestContainers.MongoDbContainer.GetConnectionString());
            _database = client.GetDatabase($"legacy_projection_race_{Guid.NewGuid():N}");
        }

        [OneTimeTearDown]
        public async Task CleanupAsync()
        {
            if (_database is not null)
            {
                await _database.Client.DropDatabaseAsync(_database.DatabaseNamespace.DatabaseName);
            }
        }

        [Test]
        public async Task Identical_snapshots_with_distinct_run_ids_can_delete_all_visible_rows()
        {
            var collection = _database.GetCollection<PaymentHistoryDocument>($"period_{Guid.NewGuid():N}");
            var accountId = Guid.NewGuid();
            var records = BuildRecords(accountId, 3, 10m);
            var firstStaged = NewSignal();
            var secondStaged = NewSignal();
            var firstCleaned = NewSignal();

            var first = LegacyRewriteAsync(
                collection,
                records,
                Guid.NewGuid(),
                afterStage: () => firstStaged.TrySetResult(),
                beforeCleanup: () => secondStaged.Task);
            var second = LegacyRewriteAsync(
                collection,
                records,
                Guid.NewGuid(),
                beforeStage: () => firstStaged.Task,
                afterStage: () => secondStaged.TrySetResult(),
                beforeCleanup: () => firstCleaned.Task);

            await secondStaged.Task;
            await first;
            firstCleaned.TrySetResult();
            await second;

            var visible = await collection.Find(FilterDefinition<PaymentHistoryDocument>.Empty).ToListAsync();
            visible.Should().BeEmpty("both legacy rewrites reported success but each cleanup removed the other run");
        }

        [Test]
        public async Task Older_snapshot_finishing_after_newer_snapshot_can_erase_newer_rows()
        {
            var collection = _database.GetCollection<PaymentHistoryDocument>($"period_{Guid.NewGuid():N}");
            var accountId = Guid.NewGuid();
            var older = BuildRecords(accountId, 2, 10m);
            var newer = BuildRecords(accountId, 4, 10m);
            var olderStaged = NewSignal();
            var newerCompleted = NewSignal();

            var olderTask = LegacyRewriteAsync(
                collection,
                older,
                Guid.NewGuid(),
                afterStage: () => olderStaged.TrySetResult(),
                beforeCleanup: () => newerCompleted.Task);
            var newerTask = LegacyRewriteAsync(
                collection,
                newer,
                Guid.NewGuid(),
                beforeStage: () => olderStaged.Task,
                afterCleanup: () => newerCompleted.TrySetResult());

            await Task.WhenAll(olderTask, newerTask);

            var visible = await collection.Find(FilterDefinition<PaymentHistoryDocument>.Empty).ToListAsync();
            visible.Should().BeEmpty("the resumed older cleanup deleted the already committed newer run");
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static IReadOnlyCollection<PaymentOperationHistoryRecord> BuildRecords(
            Guid accountId,
            int count,
            decimal amount)
        {
            return Enumerable.Range(0, count)
                .Select(index => new PaymentOperationHistoryRecord
                {
                    Balance = amount * (index + 1),
                    StreamRevision = index,
                    Record = new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        PaymentAccountId = accountId,
                        Amount = amount,
                        OperationDay = new DateOnly(2026, 1, index + 1)
                    }
                })
                .ToArray();
        }

        private static async Task LegacyRewriteAsync(
            IMongoCollection<PaymentHistoryDocument> collection,
            IReadOnlyCollection<PaymentOperationHistoryRecord> records,
            Guid runId,
            Func<Task> beforeStage = null,
            Action afterStage = null,
            Func<Task> beforeCleanup = null,
            Action afterCleanup = null)
        {
            if (beforeStage is not null)
            {
                await beforeStage();
            }

            var writes = records.Select(record =>
                new UpdateOneModel<PaymentHistoryDocument>(
                    Builders<PaymentHistoryDocument>.Filter.Eq(
                        document => document.Payload.Record.Key,
                        record.Record.Key),
                    Builders<PaymentHistoryDocument>.Update
                        .Set(document => document.Payload, record)
                        .Set(document => document.ProjectionRunId, runId))
                {
                    IsUpsert = true
                });
            await collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = true });
            afterStage?.Invoke();

            if (beforeCleanup is not null)
            {
                await beforeCleanup();
            }

            await collection.DeleteManyAsync(
                Builders<PaymentHistoryDocument>.Filter.Ne(document => document.ProjectionRunId, runId));
            afterCleanup?.Invoke();
        }
    }
}
