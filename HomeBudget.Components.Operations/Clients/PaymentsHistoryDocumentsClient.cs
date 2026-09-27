using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using MongoDB.Driver;

using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Extensions;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Core.Observability;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Operations.Clients
{
    internal class PaymentsHistoryDocumentsClient(IOptions<MongoDbOptions> dbOptions)
    : BaseDocumentClient(dbOptions?.Value, dbOptions?.Value?.PaymentsHistory), IPaymentsHistoryDocumentsClient
    {
        private const string ProjectionAuditCollectionName = "_projection_audit";
        private const string ProjectionGenerationsCollectionName = "_payment_history_generations";
        private const string ProjectionHeadsCollectionName = "_payment_history_projection_heads";
        private const string ProjectionAccountsCollectionName = "_payment_history_projection_accounts";
        private const string ProjectionRunIdIndexName = "ix_payments_history_projection_run_id";
        private const string ProjectionAuditRunIdIndexName = "ux_projection_audit_run_id";
        private const string ProjectionGenerationRecordIndexName = "ux_projection_generation_record";
        private const string ProjectionHeadPeriodIndexName = "ux_projection_head_period";
        private const string ProjectionHeadAccountIndexName = "ix_projection_head_account";
        private const string ProjectionAccountIndexName = "ux_projection_account";
        private const string TimelineDateIndexName = "ix_payments_history_timeline_date_order";
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
                        var source = await GetPaymentAccountReadSourceForPeriodAsync(collectionName);
                        var documents = await source.Collection.Find(source.ScopeFilter).ToListAsync();
                        return OrderHistoryDocuments(documents);
                    },
                    accountId);
            }

            return await TraceMongoAsync(
                "find_all_periods",
                "payments_history",
                async () =>
                {
                    var sources = await GetPaymentAccountReadSourcesAsync(accountId);
                    return await FilterByAsync(sources, FilterDefinition<PaymentHistoryDocument>.Empty);
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
                    var sources = (await GetPaymentAccountReadSourcesAsync(accountId)).ToArray();
                    if (sources.Length == 0)
                    {
                        return new PaymentHistoryQueryResult(Array.Empty<PaymentHistoryDocument>(), 0);
                    }

                    var filter = CreateQueryFilter(query);
                    var totalCounts = await Task.WhenAll(sources.Select(source =>
                        source.Collection.CountDocumentsAsync(
                            Combine(source.ScopeFilter, filter),
                            cancellationToken: cancellationToken)));
                    var totalCount = totalCounts.Sum();
                    var skip = checked((query.Page - 1) * query.PageSize);
                    var items = await MergePageAsync(sources, filter, query, skip, cancellationToken);

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
                    var source = await GetPaymentAccountReadSourceForPeriodAsync(financialPeriodIdentifier);
                    var documents = await source.Collection.Find(source.ScopeFilter).ToListAsync();
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
                    var sources = await GetPaymentAccountReadSourcesAsync(accountId);
                    var tasks = sources.Select(async source =>
                    {
                        var sort = Builders<PaymentHistoryDocument>.Sort
                            .Descending(d => d.Payload.Record.OperationDay)
                            .Descending(d => d.Payload.Record.OperationUnixTime)
                            .Descending(d => d.Payload.StreamRevision)
                            .Descending(d => d.Payload.Record.Key);
                        return await source.Collection.Find(source.ScopeFilter)
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
                    var sources = await GetPaymentAccountReadSourcesAsync(accountId);
                    var payload = await FilterByAsync(
                        sources,
                        new ExpressionFilterDefinition<PaymentHistoryDocument>(
                            document => document.Payload.Record.Key == operationId));

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

        public async Task<ProjectionPublicationResult> PublishSnapshotAsync(
            PaymentHistoryProjectionSnapshot snapshot,
            Guid projectionRunId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var generationId = CreateGenerationId(snapshot);
            var records = snapshot.Records
                .Where(static record => record?.Record != null)
                .ToArray();

            await StageGenerationAsync(generationId, projectionRunId, records, cancellationToken);
            await ValidateStagedGenerationAsync(generationId, snapshot, cancellationToken);
            return await PublishHeadAsync(snapshot, generationId, cancellationToken);
        }

        public async Task<AccountProjectionBalanceSnapshot> CreateAccountBalanceSnapshotAsync(
            Guid accountId,
            CancellationToken cancellationToken)
        {
            var fence = await AllocateAccountBalanceFenceAsync(accountId, cancellationToken);
            var periodBalances = await GetAllPeriodBalancesForAccountAsync(accountId);
            var projectedBalance = periodBalances
                .Where(static document => document?.Payload != null)
                .Sum(static document => document.Payload.Balance);

            return new AccountProjectionBalanceSnapshot(fence, projectedBalance);
        }

        public async Task<IReadOnlyCollection<PaymentHistoryProjectionScope>> DiscoverProjectionScopesAsync(
            CancellationToken cancellationToken)
        {
            using var namesCursor = await MongoDatabase.ListCollectionNamesAsync(
                cancellationToken: cancellationToken);
            var collectionNames = await namesCursor.ToListAsync(cancellationToken);
            var heads = collectionNames.Contains(ProjectionHeadsCollectionName, StringComparer.Ordinal)
                ? await MongoDatabase
                    .GetCollection<PaymentHistoryProjectionHeadDocument>(ProjectionHeadsCollectionName)
                    .Find(FilterDefinition<PaymentHistoryProjectionHeadDocument>.Empty)
                    .ToListAsync(cancellationToken)
                : [];
            var scopes = heads.ToDictionary(
                static document => document.Payload.FinancialPeriodIdentifier,
                static document => new PaymentHistoryProjectionScope(
                    document.Payload.PaymentAccountId,
                    document.Payload.FinancialPeriodIdentifier,
                    document.Payload.StreamId,
                    document.Payload.SourceRevision),
                StringComparer.Ordinal);
            foreach (var name in collectionNames)
            {
                if (scopes.ContainsKey(name) || name.Length <= 36 ||
                    !Guid.TryParse(name[..36], out var accountId) ||
                    name[36] != '-')
                {
                    continue;
                }

                scopes[name] = new PaymentHistoryProjectionScope(
                    accountId,
                    name,
                    PaymentOperationNamesGenerator.GenerateForAccountMonthStream(name),
                    null);
            }

            return scopes.Values
                .OrderBy(static scope => scope.PaymentAccountId)
                .ThenBy(static scope => scope.FinancialPeriodIdentifier, StringComparer.Ordinal)
                .ToArray();
        }

        private async Task StageGenerationAsync(
            string generationId,
            Guid projectionRunId,
            IReadOnlyCollection<PaymentOperationHistoryRecord> records,
            CancellationToken cancellationToken)
        {
            var collection = await GetProjectionGenerationsCollectionAsync();
            if (records.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var writes = records.Select(record =>
                new UpdateOneModel<PaymentHistoryDocument>(
                    Builders<PaymentHistoryDocument>.Filter.And(
                        Builders<PaymentHistoryDocument>.Filter.Eq(
                            document => document.GenerationId,
                            generationId),
                        Builders<PaymentHistoryDocument>.Filter.Eq(
                            document => document.Payload.Record.Key,
                            record.Record.Key)),
                    Builders<PaymentHistoryDocument>.Update
                        .SetOnInsert(document => document.Payload, record)
                        .SetOnInsert(document => document.GenerationId, generationId)
                        .SetOnInsert(document => document.ProjectionRunId, projectionRunId)
                        .SetOnInsert(document => document.CreatedUtc, now)
                        .SetOnInsert(document => document.UpdatedUtc, now))
                {
                    IsUpsert = true
                })
                .ToArray();

            foreach (var chunk in writes.Chunk(DbOptions.BulkInsertChunkSize))
            {
                try
                {
                    await collection.BulkWriteAsync(
                        chunk,
                        new BulkWriteOptions { IsOrdered = false },
                        cancellationToken);
                }
                catch (MongoBulkWriteException<PaymentHistoryDocument> ex)
                    when (ex.WriteErrors.Count > 0 &&
                        ex.WriteErrors.All(static error =>
                            error.Category == ServerErrorCategory.DuplicateKey))
                {
                    // Concurrent identical generation writers may lose an upsert race.
                    // Full generation validation below is the correctness boundary.
                }
            }
        }

        private async Task ValidateStagedGenerationAsync(
            string generationId,
            PaymentHistoryProjectionSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            var collection = await GetProjectionGenerationsCollectionAsync();
            var documents = await collection
                .Find(document => document.GenerationId == generationId)
                .ToListAsync(cancellationToken);
            var records = documents
                .Where(static document => document?.Payload?.Record != null)
                .Select(static document => document.Payload)
                .ToArray();

            if (records.Length != snapshot.Records.Count ||
                !string.Equals(
                    PaymentHistoryProjectionFingerprint.Create(records),
                    snapshot.SnapshotHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Staged projection generation '{generationId}' is incomplete or does not match its source snapshot.");
            }
        }

        private async Task<ProjectionPublicationResult> PublishHeadAsync(
            PaymentHistoryProjectionSnapshot snapshot,
            string generationId,
            CancellationToken cancellationToken)
        {
            var collection = await GetProjectionHeadsCollectionAsync();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var current = await collection
                    .Find(document => document.Payload.FinancialPeriodIdentifier == snapshot.FinancialPeriodIdentifier)
                    .SingleOrDefaultAsync(cancellationToken);
                var decision = DecidePublication(current?.Payload, snapshot, generationId);
                if (decision is not null)
                {
                    return decision;
                }

                var replacement = BuildHeadDocument(current, snapshot, generationId);
                if (current is null)
                {
                    try
                    {
                        await collection.InsertOneAsync(replacement, cancellationToken: cancellationToken);
                        return new(ProjectionPublicationState.Published, generationId, snapshot.SourceRevision);
                    }
                    catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        continue;
                    }
                }

                var filter = Builders<PaymentHistoryProjectionHeadDocument>.Filter.And(
                    Builders<PaymentHistoryProjectionHeadDocument>.Filter.Eq(document => document.Id, current.Id),
                    Builders<PaymentHistoryProjectionHeadDocument>.Filter.Eq(
                        document => document.Payload.SourceRevision,
                        current.Payload.SourceRevision),
                    Builders<PaymentHistoryProjectionHeadDocument>.Filter.Eq(
                        document => document.Payload.GenerationId,
                        current.Payload.GenerationId));
                var result = await collection.ReplaceOneAsync(
                    filter,
                    replacement,
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1)
                {
                    return new(ProjectionPublicationState.Published, generationId, snapshot.SourceRevision);
                }
            }

            throw new InvalidOperationException(
                $"Projection head for '{snapshot.FinancialPeriodIdentifier}' changed repeatedly during publication.");
        }

        private static ProjectionPublicationResult DecidePublication(
            PaymentHistoryProjectionHead current,
            PaymentHistoryProjectionSnapshot candidate,
            string generationId)
        {
            if (current is null)
            {
                return null;
            }

            if (!string.Equals(current.StreamId, candidate.StreamId, StringComparison.Ordinal) ||
                current.PaymentAccountId != candidate.PaymentAccountId)
            {
                throw new InvalidOperationException(
                    $"Projection scope '{candidate.FinancialPeriodIdentifier}' is already bound to a different source stream or account.");
            }

            if (current.SourceRevision > candidate.SourceRevision)
            {
                return new(ProjectionPublicationState.Superseded, current.GenerationId, current.SourceRevision);
            }

            if (current.SourceRevision != candidate.SourceRevision)
            {
                return null;
            }

            if (!string.Equals(current.SnapshotHash, candidate.SnapshotHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Projection source revision {candidate.SourceRevision} for '{candidate.StreamId}' produced conflicting snapshot content.");
            }

            return new(ProjectionPublicationState.AlreadyPublished, generationId, current.SourceRevision);
        }

        private static PaymentHistoryProjectionHeadDocument BuildHeadDocument(
            PaymentHistoryProjectionHeadDocument current,
            PaymentHistoryProjectionSnapshot snapshot,
            string generationId)
        {
            return new PaymentHistoryProjectionHeadDocument
            {
                Id = current?.Id ?? default,
                CreatedUtc = current?.CreatedUtc ?? DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                Payload = new PaymentHistoryProjectionHead
                {
                    FinancialPeriodIdentifier = snapshot.FinancialPeriodIdentifier,
                    PaymentAccountId = snapshot.PaymentAccountId,
                    StreamId = snapshot.StreamId,
                    SourceRevision = snapshot.SourceRevision,
                    SourcePosition = snapshot.SourcePosition,
                    SnapshotHash = snapshot.SnapshotHash,
                    GenerationId = generationId,
                    RecordCount = snapshot.Records.Count,
                    FinalBalance = snapshot.FinalBalance,
                    PublishedUtc = DateTime.UtcNow
                }
            };
        }

        private async Task<long> AllocateAccountBalanceFenceAsync(
            Guid accountId,
            CancellationToken cancellationToken)
        {
            var collection = await GetProjectionAccountsCollectionAsync();
            var filter = Builders<PaymentHistoryProjectionAccountDocument>.Filter.Eq(
                document => document.Payload.PaymentAccountId,
                accountId);
            var update = Builders<PaymentHistoryProjectionAccountDocument>.Update
                .SetOnInsert(document => document.Payload.PaymentAccountId, accountId)
                .SetOnInsert(document => document.CreatedUtc, DateTime.UtcNow)
                .Set(document => document.UpdatedUtc, DateTime.UtcNow)
                .Inc(document => document.Payload.NextBalanceFence, 1);
            var updated = await collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<PaymentHistoryProjectionAccountDocument>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);

            return updated.Payload.NextBalanceFence;
        }

        private static string CreateGenerationId(PaymentHistoryProjectionSnapshot snapshot)
        {
            var value = string.Join(
                "|",
                snapshot.StreamId,
                snapshot.SourceRevision.ToString(CultureInfo.InvariantCulture),
                snapshot.SnapshotHash);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
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

        public async Task RecordProjectionPublicationAsync(
            Guid projectionRunId,
            string publicationState)
        {
            await TraceMongoAsync(
                "projection_audit_publication",
                ProjectionAuditCollectionName,
                async () =>
                {
                    var collection = await GetProjectionAuditCollectionAsync();
                    var now = DateTime.UtcNow;
                    var filter = Builders<ProjectionAuditDocument>.Filter
                        .Eq(document => document.Payload.ProjectionRunId, projectionRunId);
                    var update = Builders<ProjectionAuditDocument>.Update
                        .Set(document => document.Payload.Status, "Published")
                        .Set(document => document.Payload.PublicationState, publicationState)
                        .Set(document => document.Payload.UpdatedUtc, now)
                        .Set(document => document.UpdatedUtc, now);

                    await collection.UpdateOneAsync(filter, update);

                    return true;
                });
        }

        public async Task CompleteProjectionRunAsync(
            Guid projectionRunId,
            string status,
            string error = null,
            string publicationState = null)
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
                    var updates = new List<UpdateDefinition<ProjectionAuditDocument>> { update };
                    if (publicationState is not null)
                    {
                        updates.Add(Builders<ProjectionAuditDocument>.Update
                            .Set(d => d.Payload.PublicationState, publicationState));
                    }

                    await collection.UpdateOneAsync(
                        filter,
                        Builders<ProjectionAuditDocument>.Update.Combine(updates));

                    return true;
                });
        }

        private async Task<IMongoCollection<PaymentHistoryDocument>> GetProjectionGenerationsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<PaymentHistoryDocument>(ProjectionGenerationsCollectionName);
            await EnsureNamedIndexAsync(
                collection,
                ProjectionGenerationRecordIndexName,
                Builders<PaymentHistoryDocument>.IndexKeys
                    .Ascending(document => document.GenerationId)
                    .Ascending(document => document.Payload.Record.Key),
                unique: true);
            await EnsureTimelineIndexesAsync(collection);
            return collection;
        }

        private async Task<IMongoCollection<PaymentHistoryProjectionHeadDocument>> GetProjectionHeadsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<PaymentHistoryProjectionHeadDocument>(ProjectionHeadsCollectionName);
            await EnsureNamedIndexAsync(
                collection,
                ProjectionHeadPeriodIndexName,
                Builders<PaymentHistoryProjectionHeadDocument>.IndexKeys.Ascending(
                    document => document.Payload.FinancialPeriodIdentifier),
                unique: true);
            await EnsureNamedIndexAsync(
                collection,
                ProjectionHeadAccountIndexName,
                Builders<PaymentHistoryProjectionHeadDocument>.IndexKeys.Ascending(
                    document => document.Payload.PaymentAccountId),
                unique: false);
            return collection;
        }

        private async Task<IMongoCollection<PaymentHistoryProjectionAccountDocument>> GetProjectionAccountsCollectionAsync()
        {
            var collection = MongoDatabase.GetCollection<PaymentHistoryProjectionAccountDocument>(ProjectionAccountsCollectionName);
            await EnsureNamedIndexAsync(
                collection,
                ProjectionAccountIndexName,
                Builders<PaymentHistoryProjectionAccountDocument>.IndexKeys.Ascending(
                    document => document.Payload.PaymentAccountId),
                unique: true);
            return collection;
        }

        private static async Task EnsureNamedIndexAsync<TDocument>(
            IMongoCollection<TDocument> collection,
            string name,
            IndexKeysDefinition<TDocument> keys,
            bool unique)
        {
            var indexes = await collection.Indexes.List().ToListAsync();
            if (indexes.Any(index => index.GetValue("name", string.Empty).AsString == name))
            {
                return;
            }

            try
            {
                await collection.Indexes.CreateOneAsync(new CreateIndexModel<TDocument>(
                    keys,
                    new CreateIndexOptions { Name = name, Unique = unique }));
            }
            catch (MongoCommandException ex) when (ex.Code is 85 or 86)
            {
                var refreshed = await collection.Indexes.List().ToListAsync();
                if (!refreshed.Any(index => index.GetValue("name", string.Empty).AsString == name))
                {
                    throw;
                }
            }
        }

        private async Task<IEnumerable<HistoryReadSource>> GetPaymentAccountReadSourcesAsync(Guid accountId)
        {
            var headsCollection = await GetProjectionHeadsCollectionAsync();
            var heads = await headsCollection
                .Find(document => document.Payload.PaymentAccountId == accountId)
                .ToListAsync();
            var publishedPeriods = heads
                .Select(static document => document.Payload.FinancialPeriodIdentifier)
                .ToHashSet(StringComparer.Ordinal);
            var databaseCollectionNames = await MongoDatabase.ListCollectionNamesAsync();
            var dbCollections = await databaseCollectionNames.ToListAsync();
            var legacyCollections = dbCollections
                .Where(name => name.StartsWith(accountId.ToString(), StringComparison.OrdinalIgnoreCase))
                .Where(name => !publishedPeriods.Contains(name))
                .Select(name => MongoDatabase.GetCollection<PaymentHistoryDocument>(name))
                .ToArray();

            await Task.WhenAll(legacyCollections.Select(EnsureTimelineIndexesAsync));

            var sources = legacyCollections
                .Select(static collection => new HistoryReadSource(
                    collection,
                    FilterDefinition<PaymentHistoryDocument>.Empty))
                .ToList();
            if (heads.Count == 0)
            {
                return sources;
            }

            var generations = await GetProjectionGenerationsCollectionAsync();
            sources.AddRange(heads.Select(head => new HistoryReadSource(
                generations,
                Builders<PaymentHistoryDocument>.Filter.Eq(
                    document => document.GenerationId,
                    head.Payload.GenerationId))));
            return sources;
        }

        private async Task<HistoryReadSource> GetPaymentAccountReadSourceForPeriodAsync(
            string financialPeriodIdentifier)
        {
            var heads = await GetProjectionHeadsCollectionAsync();
            var head = await heads
                .Find(document => document.Payload.FinancialPeriodIdentifier == financialPeriodIdentifier)
                .SingleOrDefaultAsync();
            if (head is null)
            {
                return new HistoryReadSource(
                    await GetPaymentAccountCollectionForPeriodAsync(financialPeriodIdentifier),
                    FilterDefinition<PaymentHistoryDocument>.Empty);
            }

            return new HistoryReadSource(
                await GetProjectionGenerationsCollectionAsync(),
                Builders<PaymentHistoryDocument>.Filter.Eq(
                    document => document.GenerationId,
                    head.Payload.GenerationId));
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
            IEnumerable<HistoryReadSource> sources,
            FilterDefinition<PaymentHistoryDocument> filter)
        {
            var tasks = sources.Select(async source => await source.Collection
                .Find(Combine(source.ScopeFilter, filter))
                .ToListAsync());
            var results = await Task.WhenAll(tasks);

            return OrderHistoryDocuments(results.SelectMany(static documents => documents)).AsReadOnly();
        }

        private static FilterDefinition<PaymentHistoryDocument> Combine(
            FilterDefinition<PaymentHistoryDocument> left,
            FilterDefinition<PaymentHistoryDocument> right)
        {
            return Builders<PaymentHistoryDocument>.Filter.And(left, right);
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
            IEnumerable<HistoryReadSource> sources,
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
                foreach (var source in sources)
                {
                    var cursor = await source.Collection.FindAsync(
                        Combine(source.ScopeFilter, filter),
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
                    ? sort.Descending(document => document.Payload.Record.OperationDay)
                        .Descending(document => document.Payload.Record.OperationUnixTime)
                        .Descending(document => document.Payload.StreamRevision)
                        .Descending(document => document.Payload.Record.Key)
                    : sort.Ascending(document => document.Payload.Record.OperationDay)
                        .Ascending(document => document.Payload.Record.OperationUnixTime)
                        .Ascending(document => document.Payload.StreamRevision)
                        .Ascending(document => document.Payload.Record.Key)
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
                var comparison = query.SortBy == PaymentHistorySortField.Amount
                    ? leftRecord.Amount.CompareTo(rightRecord.Amount)
                    : CompareCanonicalHistoryOrder(left, right);
                if (comparison == 0 && query.SortBy == PaymentHistorySortField.Amount)
                {
                    comparison = leftRecord.Key.CompareTo(rightRecord.Key);
                }

                return query.SortDirection == PaymentHistorySortDirection.Desc ? -comparison : comparison;
            }

            private static int CompareCanonicalHistoryOrder(HistoryCursorHead left, HistoryCursorHead right)
            {
                var leftRecord = left.Current.Payload.Record;
                var rightRecord = right.Current.Payload.Record;
                var dayComparison = leftRecord.OperationDay.CompareTo(rightRecord.OperationDay);
                if (dayComparison != 0)
                {
                    return dayComparison;
                }

                var timestampComparison = leftRecord.OperationUnixTime.CompareTo(rightRecord.OperationUnixTime);
                if (timestampComparison != 0)
                {
                    return timestampComparison;
                }

                var revisionComparison = Nullable.Compare(
                    left.Current.Payload.StreamRevision,
                    right.Current.Payload.StreamRevision);
                return revisionComparison != 0
                    ? revisionComparison
                    : leftRecord.Key.CompareTo(rightRecord.Key);
            }
        }

        private sealed record HistoryReadSource(
            IMongoCollection<PaymentHistoryDocument> Collection,
            FilterDefinition<PaymentHistoryDocument> ScopeFilter);

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
                        .Ascending(document => document.Payload.Record.OperationUnixTime)
                        .Ascending(document => document.Payload.StreamRevision)
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
