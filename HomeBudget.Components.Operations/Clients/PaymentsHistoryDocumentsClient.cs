using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentsHistoryDocumentsClient(IOptions<MongoDbOptions> dbOptions)
    : BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.PaymentsHistory), IPaymentsHistoryDocumentsClient
    {
        private const string ProjectionAuditCollectionName = "_projection_audit";
        private const string ProjectionRunIdIndexName = "ix_payments_history_projection_run_id";
        private const string ProjectionAuditRunIdIndexName = "ux_projection_audit_run_id";
        private const string TimelineDateIndexName = "ix_payments_history_timeline_date";
        private const string TimelineAmountIndexName = "ix_payments_history_timeline_amount";

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
                        var documents = await targetCollection.Find(_ => true).ToListAsync();
                        return OrderHistoryDocuments(documents);
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

        public async Task<PaymentHistoryQueryResult> QueryAsync(
            Guid accountId,
            PaymentHistoryQuery query,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);

            return await TraceMongoAsync(
                "query_timeline",
                "payments_history",
                async () =>
                {
                    var collections = (await GetPaymentAccountCollectionsAsync(accountId)).ToArray();
                    if (collections.Length == 0)
                    {
                        return new PaymentHistoryQueryResult(Array.Empty<PaymentHistoryDocument>(), 0);
                    }

                    var filter = CreateQueryFilter(query);
                    var totalCounts = await Task.WhenAll(collections.Select(collection =>
                        collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken)));
                    var totalCount = totalCounts.Sum();
                    var skip = checked((query.Page - 1) * query.PageSize);
                    var items = await MergePageAsync(collections, filter, query, skip, cancellationToken);

                    return new PaymentHistoryQueryResult(items, totalCount);
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

                    var documents = await targetCollection.Find(FilterDefinition<PaymentHistoryDocument>.Empty).ToListAsync();
                    return OrderHistoryDocuments(documents).LastOrDefault();
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
                    var tasks = targetCollections.Select(async collection =>
                    {
                        var sort = Builders<PaymentHistoryDocument>.Sort
                            .Descending(d => d.Payload.Record.OperationDay)
                            .Descending(d => d.Payload.Record.OperationUnixTime)
                            .Descending(d => d.Payload.StreamRevision)
                            .Descending(d => d.Payload.Record.Key);
                        return await collection.Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                            .Sort(sort)
                            .Limit(1)
                            .FirstOrDefaultAsync();
                    });

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
            await ReplaceOneAsync(financialPeriodIdentifier, payload);
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
                        .Update
                        .Set(d => d.Payload, payload)
                        .Set(d => d.UpdatedUtc, DateTime.UtcNow)
                        .SetOnInsert(d => d.CreatedUtc, DateTime.UtcNow);

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
                                .Set(d => d.Payload, r)
                                .Set(d => d.UpdatedUtc, DateTime.UtcNow)
                                .SetOnInsert(d => d.CreatedUtc, DateTime.UtcNow))
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

        public async Task RewriteAllAsync(
            string financialPeriodIdentifier,
            IEnumerable<PaymentOperationHistoryRecord> operationHistoryRecords,
            Guid projectionRunId)
        {
            var records = (operationHistoryRecords ?? [])
                .Where(static x => x?.Record != null)
                .ToList();

            await TraceMongoAsync(
                "projection_replace",
                financialPeriodIdentifier,
                async () =>
                {
                    var targetCollection = await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier);
                    var now = DateTime.UtcNow;

                    if (records.Count > 0)
                    {
                        var bulkOps = records.Select(r =>
                            new UpdateOneModel<PaymentHistoryDocument>(
                                Builders<PaymentHistoryDocument>.Filter
                                    .Eq(d => d.Payload.Record.Key, r.Record.Key),
                                Builders<PaymentHistoryDocument>.Update
                                    .Set(d => d.Payload, r)
                                    .Set(d => d.ProjectionRunId, projectionRunId)
                                    .Set(d => d.UpdatedUtc, now)
                                    .SetOnInsert(d => d.CreatedUtc, now))
                            {
                                IsUpsert = true
                            })
                            .ToList();

                        foreach (var chunk in bulkOps.Chunk(DbOptions.BulkInsertChunkSize))
                        {
                            await targetCollection.BulkWriteAsync(
                                chunk,
                                new BulkWriteOptions
                                {
                                    IsOrdered = true
                                });
                        }
                    }

                    await targetCollection.DeleteManyAsync(
                        Builders<PaymentHistoryDocument>.Filter.Ne(d => d.ProjectionRunId, projectionRunId));

                    return true;
                },
                records.FirstOrDefault()?.Record.PaymentAccountId);
        }

        public async Task BeginProjectionRunAsync(ProjectionAuditRecord auditRecord)
        {
            ArgumentNullException.ThrowIfNull(auditRecord);

            await TraceMongoAsync(
                "projection_audit_begin",
                ProjectionAuditCollectionName,
                async () =>
                {
                    var collection = await GetProjectionAuditCollectionAsync();
                    await collection.InsertOneAsync(new ProjectionAuditDocument
                    {
                        Payload = auditRecord
                    });

                    return true;
                });
        }

        public async Task CompleteProjectionRunAsync(Guid projectionRunId, string status, string error = null)
        {
            await TraceMongoAsync(
                "projection_audit_complete",
                ProjectionAuditCollectionName,
                async () =>
                {
                    var collection = await GetProjectionAuditCollectionAsync();
                    var now = DateTime.UtcNow;
                    var filter = Builders<ProjectionAuditDocument>.Filter
                        .Eq(d => d.Payload.ProjectionRunId, projectionRunId);
                    var update = Builders<ProjectionAuditDocument>.Update
                        .Set(d => d.Payload.Status, status)
                        .Set(d => d.Payload.Error, error)
                        .Set(d => d.Payload.UpdatedUtc, now)
                        .Set(d => d.Payload.CompletedUtc, now)
                        .Set(d => d.UpdatedUtc, now);

                    await collection.UpdateOneAsync(filter, update);

                    return true;
                });
        }

        private async Task<IEnumerable<IMongoCollection<PaymentHistoryDocument>>> GetPaymentAccountCollectionsAsync(Guid accountId)
        {
            var databaseCollectionNames = await MongoDatabase.ListCollectionNamesAsync();
            var dbCollections = await databaseCollectionNames.ToListAsync();
            var paymentAccountCollections = dbCollections.Where(name => name.StartsWith(accountId.ToString(), StringComparison.OrdinalIgnoreCase));

            var collections = paymentAccountCollections
                .Select(collectionName => MongoDatabase.GetCollection<PaymentHistoryDocument>(collectionName))
                .ToArray();

            await Task.WhenAll(collections.Select(EnsureTimelineIndexesAsync));

            return collections;
        }

        private async Task<IMongoCollection<PaymentHistoryDocument>> GetPaymentAccountCollectionForPeriodAsync(string financialPeriodIdentifier)
        {
            var collection = MongoDatabase.GetCollection<PaymentHistoryDocument>(financialPeriodIdentifier);
            await EnsureUniqueIndexAsync(collection, "Payload.Record.Key", "ux_payments_history_record_key");
            await EnsureNonUniqueIndexAsync(collection, "ProjectionRunId", ProjectionRunIdIndexName);
            await EnsureTimelineIndexesAsync(collection);

            return collection;
        }

        private async Task<IMongoCollection<ProjectionAuditDocument>> GetProjectionAuditCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<ProjectionAuditDocument>(ProjectionAuditCollectionName);
            await EnsureUniqueIndexAsync(collection, "Payload.ProjectionRunId", ProjectionAuditRunIdIndexName);

            return collection;
        }

        private static async Task<IReadOnlyCollection<PaymentHistoryDocument>> FilterByAsync(
            IEnumerable<IMongoCollection<PaymentHistoryDocument>> collections,
            FilterDefinition<PaymentHistoryDocument> filter)
        {
            var tasks = collections.Select(async collection => await collection.Find(filter).ToListAsync());
            var results = await Task.WhenAll(tasks);

            return OrderHistoryDocuments(results.SelectMany(static documents => documents)).AsReadOnly();
        }

        private static List<PaymentHistoryDocument> OrderHistoryDocuments(IEnumerable<PaymentHistoryDocument> documents)
        {
            return documents
                .Where(static document => document?.Payload?.Record != null)
                .OrderByHistoryOrder()
                .ToList();
        }

        private static FilterDefinition<PaymentHistoryDocument> CreateQueryFilter(PaymentHistoryQuery query)
        {
            var filter = Builders<PaymentHistoryDocument>.Filter;
            var filters = new List<FilterDefinition<PaymentHistoryDocument>>();

            if (query.DateFrom.HasValue)
            {
                filters.Add(filter.Gte(document => document.Payload.Record.OperationDay, query.DateFrom.Value));
            }

            if (query.DateTo.HasValue)
            {
                filters.Add(filter.Lte(document => document.Payload.Record.OperationDay, query.DateTo.Value));
            }

            if (query.HasCategoryFilter)
            {
                filters.Add(query.CategoryIds.Count > 0
                    ? filter.In(document => document.Payload.Record.CategoryId, query.CategoryIds)
                    : filter.Eq(document => document.Payload.Record.Key, Guid.Empty));
            }

            if (query.ContractorId.HasValue)
            {
                filters.Add(filter.Eq(document => document.Payload.Record.ContractorId, query.ContractorId.Value));
            }

            if (query.AmountMin.HasValue)
            {
                filters.Add(filter.Gte(document => document.Payload.Record.Amount, query.AmountMin.Value));
            }

            if (query.AmountMax.HasValue)
            {
                filters.Add(filter.Lte(document => document.Payload.Record.Amount, query.AmountMax.Value));
            }

            return filters.Count == 0 ? FilterDefinition<PaymentHistoryDocument>.Empty : filter.And(filters);
        }

        private static async Task<IReadOnlyCollection<PaymentHistoryDocument>> MergePageAsync(
            IEnumerable<IMongoCollection<PaymentHistoryDocument>> collections,
            FilterDefinition<PaymentHistoryDocument> filter,
            PaymentHistoryQuery query,
            int skip,
            CancellationToken cancellationToken)
        {
            var cursors = new List<IAsyncCursor<PaymentHistoryDocument>>();
            var heads = new List<HistoryCursorHead>();

            try
            {
                var sort = CreateQuerySort(query);
                foreach (var collection in collections)
                {
                    var cursor = await collection.FindAsync(
                        filter,
                        new FindOptions<PaymentHistoryDocument>
                        {
                            Sort = sort,
                            BatchSize = query.PageSize
                        },
                        cancellationToken);
                    cursors.Add(cursor);
                    var head = new HistoryCursorHead(cursor);
                    if (await head.MoveNextAsync(cancellationToken))
                    {
                        heads.Add(head);
                    }
                }

                var comparer = new HistoryCursorHeadComparer(query);
                var queue = new PriorityQueue<HistoryCursorHead, HistoryCursorHead>(comparer);
                foreach (var head in heads)
                {
                    queue.Enqueue(head, head);
                }

                var items = new List<PaymentHistoryDocument>(query.PageSize);
                var seen = 0;
                while (queue.TryDequeue(out var head, out _))
                {
                    if (seen >= skip && items.Count < query.PageSize)
                    {
                        items.Add(head.Current);
                    }

                    seen++;
                    if (items.Count == query.PageSize)
                    {
                        break;
                    }

                    if (await head.MoveNextAsync(cancellationToken))
                    {
                        queue.Enqueue(head, head);
                    }
                }

                return items.AsReadOnly();
            }
            finally
            {
                foreach (var cursor in cursors)
                {
                    cursor.Dispose();
                }
            }
        }

        private static SortDefinition<PaymentHistoryDocument> CreateQuerySort(PaymentHistoryQuery query)
        {
            var sort = Builders<PaymentHistoryDocument>.Sort;
            var descending = query.SortDirection == PaymentHistorySortDirection.Desc;

            return query.SortBy switch
            {
                PaymentHistorySortField.Amount => descending
                    ? sort.Descending(document => document.Payload.Record.Amount).Descending(document => document.Payload.Record.Key)
                    : sort.Ascending(document => document.Payload.Record.Amount).Ascending(document => document.Payload.Record.Key),
                _ => descending
                    ? sort.Descending(document => document.Payload.Record.OperationDay).Descending(document => document.Payload.Record.Key)
                    : sort.Ascending(document => document.Payload.Record.OperationDay).Ascending(document => document.Payload.Record.Key)
            };
        }

        private sealed class HistoryCursorHead(IAsyncCursor<PaymentHistoryDocument> cursor)
        {
            private IEnumerator<PaymentHistoryDocument> _batchEnumerator;

            public PaymentHistoryDocument Current { get; private set; }

            public async Task<bool> MoveNextAsync(CancellationToken cancellationToken)
            {
                while (_batchEnumerator is null || !_batchEnumerator.MoveNext())
                {
                    _batchEnumerator?.Dispose();
                    if (!await cursor.MoveNextAsync(cancellationToken))
                    {
                        return false;
                    }

                    _batchEnumerator = cursor.Current.GetEnumerator();
                }

                Current = _batchEnumerator.Current;
                return true;
            }
        }

        private sealed class HistoryCursorHeadComparer(PaymentHistoryQuery query) : IComparer<HistoryCursorHead>
        {
            public int Compare(HistoryCursorHead left, HistoryCursorHead right)
            {
                var leftRecord = left.Current.Payload.Record;
                var rightRecord = right.Current.Payload.Record;
                var first = query.SortBy == PaymentHistorySortField.Amount
                    ? leftRecord.Amount.CompareTo(rightRecord.Amount)
                    : leftRecord.OperationDay.CompareTo(rightRecord.OperationDay);
                var tieBreak = leftRecord.Key.CompareTo(rightRecord.Key);
                var comparison = first != 0 ? first : tieBreak;

                return query.SortDirection == PaymentHistorySortDirection.Desc ? -comparison : comparison;
            }
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

        private static async Task EnsureTimelineIndexesAsync(IMongoCollection<PaymentHistoryDocument> collection)
        {
            var indexes = await collection.Indexes.List().ToListAsync();
            var keys = Builders<PaymentHistoryDocument>.IndexKeys;

            if (!indexes.Any(index => index.GetValue("name", string.Empty).AsString == TimelineDateIndexName))
            {
                await collection.Indexes.CreateOneAsync(new CreateIndexModel<PaymentHistoryDocument>(
                    keys.Ascending(document => document.Payload.Record.OperationDay)
                        .Ascending(document => document.Payload.Record.Key),
                    new CreateIndexOptions { Name = TimelineDateIndexName }));
            }

            if (!indexes.Any(index => index.GetValue("name", string.Empty).AsString == TimelineAmountIndexName))
            {
                await collection.Indexes.CreateOneAsync(new CreateIndexModel<PaymentHistoryDocument>(
                    keys.Ascending(document => document.Payload.Record.Amount)
                        .Ascending(document => document.Payload.Record.Key),
                    new CreateIndexOptions { Name = TimelineAmountIndexName }));
            }
        }
    }
}
