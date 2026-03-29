using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentsHistoryDocumentsClient(IOptions<MongoDbOptions> dbOptions)
    : BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.PaymentsHistory), IPaymentsHistoryDocumentsClient
    {
        public MongoDbOptions DbOptions { get; } = dbOptions?.Value;

        public async Task<IReadOnlyCollection<PaymentHistoryDocument>> GetAsync(Guid accountId, FinancialPeriod period = null)
        {
            if (period != null)
            {
                var collectionName = period.ToFinancialMonthIdentifier(accountId);

                return await TraceMongoAsync(
                    "find",
                    collectionName,
                    async () =>
                    {
                        var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(collectionName);
                        return await targetCollection.Find(_ => true).ToListAsync();
                    },
                    accountId);
            }

            return await TraceMongoAsync(
                "find_all_periods",
                "payments_history",
                async () =>
                {
                    var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);
                    return await FilterByAsync(targetCollections, new ExpressionFilterDefinition<PaymentHistoryDocument>(_ => true));
                },
                accountId);
        }

        public async Task<PaymentHistoryDocument> GetLastForPeriodAsync(string financialPeriodIdentifier)
        {
            return await TraceMongoAsync(
                "find_last_for_period",
                financialPeriodIdentifier,
                async () =>
                {
                    var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

                    return await targetCollection.Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                        .SortByDescending(f => f.Payload.Record.OperationDay)
                        .ThenByDescending(f => f.Payload.Record.OperationUnixTime)
                        .ThenByDescending(f => f.Payload.Record.Key)
                        .Limit(1)
                        .FirstOrDefaultAsync();
                });
        }

        public async Task<IEnumerable<PaymentHistoryDocument>> GetAllPeriodBalancesForAccountAsync(Guid accountId)
        {
            return await TraceMongoAsync(
                "find_period_balances",
                "payments_history",
                async () =>
                {
                    var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);
                    var tasks = targetCollections.Select(cl => cl.Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                        .SortByDescending(f => f.Payload.Record.OperationDay)
                        .ThenByDescending(f => f.Payload.Record.OperationUnixTime)
                        .ThenByDescending(f => f.Payload.Record.Key)
                        .Limit(1)
                        .FirstOrDefaultAsync());

                    return await Task.WhenAll(tasks);
                },
                accountId);
        }

        public async Task<PaymentHistoryDocument> GetByIdAsync(Guid accountId, Guid operationId)
        {
            return await TraceMongoAsync(
                "find_by_id",
                "payments_history",
                async () =>
                {
                    var targetCollections = await GetPaymentAccountCollectionsAsync(accountId);
                    var payload = await FilterByAsync(targetCollections, new ExpressionFilterDefinition<PaymentHistoryDocument>(d => d.Payload.Record.Key == operationId));

                    return payload.SingleOrDefault();
                },
                accountId,
                operationId);
        }

        public async Task InsertOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord payload)
        {
            await TraceMongoAsync(
                "insert_one",
                financialPeriodIdentifier,
                async () =>
                {
                    var document = new PaymentHistoryDocument { Payload = payload };
                    var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

                    await targetCollection.InsertOneAsync(document);
                    return true;
                },
                payload?.Record.PaymentAccountId,
                payload?.Record.Key);
        }

        public async Task ReplaceOneAsync(string financialPeriodIdentifier, PaymentOperationHistoryRecord payload)
        {
            await TraceMongoAsync(
                "replace_one",
                financialPeriodIdentifier,
                async () =>
                {
                    var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

                    var filter = Builders<PaymentHistoryDocument>
                        .Filter.Eq(d => d.Payload.Record.Key, payload.Record.Key);

                    var update = Builders<PaymentHistoryDocument>
                        .Update.Set(d => d.Payload, payload);

                    await targetCollection.UpdateOneAsync(
                        filter,
                        update,
                        new UpdateOptions { IsUpsert = true });
                    return true;
                },
                payload?.Record.PaymentAccountId,
                payload?.Record.Key);
        }

        public async Task BulkWriteAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationHistoryRecord> payload)
        {
            var records = payload?.ToList() ?? [];

            await TraceMongoAsync(
                "bulk_write",
                financialPeriodIdentifier,
                async () =>
                {
                    var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);

                    var bulkOps = records.Select(r =>
                        new UpdateOneModel<PaymentHistoryDocument>(
                            Builders<PaymentHistoryDocument>.Filter
                                .Eq(d => d.Payload.Record.Key, r.Record.Key),
                            Builders<PaymentHistoryDocument>.Update
                                .Set(d => d.Payload, r))
                        {
                            IsUpsert = true
                        })
                        .ToList();

                    if (bulkOps.Count > 0)
                    {
                        foreach (var chunk in bulkOps.Chunk(DbOptions.BulkInsertChunkSize))
                        {
                            await targetCollection.BulkWriteAsync(
                                chunk,
                                new BulkWriteOptions
                                {
                                    IsOrdered = false
                                });
                        }
                    }

                    return true;
                },
                records.FirstOrDefault()?.Record.PaymentAccountId);
        }

        public async Task RemoveAsync(string financialPeriodIdentifier)
        {
            await TraceMongoAsync(
                "delete_many",
                financialPeriodIdentifier,
                async () =>
                {
                    var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);
                    await targetCollection.DeleteManyAsync(_ => true);
                    return true;
                });
        }

        public async Task RewriteAllAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationHistoryRecord> operationHistoryRecords)
        {
            await RemoveAsync(financialPeriodIdentifier);
            await BulkWriteAsync(financialPeriodIdentifier, operationHistoryRecords);
        }

        private async Task<IEnumerable<IMongoCollection<PaymentHistoryDocument>>> GetPaymentAccountCollectionsAsync(Guid accountId)
        {
            var databaseCollectionNames = await MongoDatabase.ListCollectionNamesAsync();
            var dbCollections = await databaseCollectionNames.ToListAsync();
            var paymentAccountCollections = dbCollections.Where(name => name.StartsWith(accountId.ToString()));

            return paymentAccountCollections.Select(collectionName => MongoDatabase.GetCollection<PaymentHistoryDocument>(collectionName));
        }

        private async Task<IMongoCollection<PaymentHistoryDocument>> GetPaymentAccountCollectionForPeriodAsync(string financialPeriodIdentifier)
        {
            var collection = MongoDatabase.GetCollection<PaymentHistoryDocument>(financialPeriodIdentifier);
            var indexKeysDefinition = Builders<PaymentHistoryDocument>.IndexKeys.Ascending(p => p.Payload.Record.Key);
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<PaymentHistoryDocument>(indexKeysDefinition));

            return collection;
        }

        private static async Task<IReadOnlyCollection<PaymentHistoryDocument>> FilterByAsync(
            IEnumerable<IMongoCollection<PaymentHistoryDocument>> collections,
            FilterDefinition<PaymentHistoryDocument> filter)
        {
            var tasks = collections.Select(async collection => await collection.Find(filter).ToListAsync());
            var results = await Task.WhenAll(tasks);

            return results.SelectMany(docs => docs).ToList().AsReadOnly();
        }

        private static async Task<T> TraceMongoAsync<T>(
            string operation,
            string collectionName,
            Func<Task<T>> action,
            Guid? accountId = null,
            Guid? operationId = null)
        {
            using var activity = ActivityPropagation.StartActivity(
                $"mongodb.{operation}",
                ActivityKind.Client);
            var startedAt = Stopwatch.StartNew();

            if (activity != null)
            {
                activity.SetTag(ActivityTags.DbSystem, "mongodb");
                activity.SetTag("db.operation", operation);
                activity.SetTag(ActivityTags.MongoCollection, collectionName);

                if (accountId.HasValue && accountId.Value != Guid.Empty)
                {
                    activity.SetAccount(accountId.Value);
                }

                if (operationId.HasValue && operationId.Value != Guid.Empty)
                {
                    activity.SetPayment(operationId.Value);
                }
            }

            try
            {
                var result = await action();

                startedAt.Stop();
                TelemetryMetrics.MongoCrudDurationMs.Record(
                    startedAt.Elapsed.TotalMilliseconds,
                    [new KeyValuePair<string, object>("operation", operation)]);
                activity?.SetStatus(ActivityStatusCode.Ok);

                return result;
            }
            catch (Exception ex)
            {
                startedAt.Stop();
                TelemetryMetrics.MongoCrudDurationMs.Record(
                    startedAt.Elapsed.TotalMilliseconds,
                    [new KeyValuePair<string, object>("operation", operation)]);
                activity?.RecordException(ex);
                throw;
            }
        }
    }
}
