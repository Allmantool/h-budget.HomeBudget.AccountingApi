using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using Microsoft.Data.SqlClient;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    internal static class PaymentProjectionWaiter
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

        public static async Task<PaymentOperationHistoryRecordResponse> WaitForHistoryRecordAsync(
            RestClient restClient,
            Guid paymentAccountId,
            Guid operationId,
            Func<PaymentOperationHistoryRecordResponse, bool> condition = null,
            string conditionDescription = null,
            CancellationToken cancellationToken = default)
        {
            PaymentOperationHistoryRecordResponse lastRecord = null;
            ApiSnapshot lastSnapshot = null;
            Exception lastException = null;
            var timeoutAt = DateTime.UtcNow + DefaultTimeout;

            while (DateTime.UtcNow < timeoutAt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    (lastRecord, lastSnapshot) = await GetHistoryRecordOnceAsync(restClient, paymentAccountId, operationId);

                    if (lastRecord is not null && (condition == null || condition(lastRecord)))
                    {
                        return lastRecord;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                await Task.Delay(DefaultPollInterval, cancellationToken);
            }

            Assert.Fail(
                BuildTimeoutMessage(
                    $"Payment history record '{operationId}' for account '{paymentAccountId}'",
                    conditionDescription ?? "record is visible in payment history",
                    lastSnapshot,
                    lastException,
                    [operationId]));

            return lastRecord;
        }

        public static async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> WaitForHistoryRecordsAsync(
            RestClient restClient,
            Guid paymentAccountId,
            Func<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>, bool> condition,
            string conditionDescription,
            IEnumerable<Guid> knownOperationIds = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<PaymentOperationHistoryRecordResponse> lastRecords = Array.Empty<PaymentOperationHistoryRecordResponse>();
            ApiSnapshot lastSnapshot = null;
            Exception lastException = null;
            var timeoutAt = DateTime.UtcNow + DefaultTimeout;

            while (DateTime.UtcNow < timeoutAt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    (lastRecords, lastSnapshot) = await GetHistoryRecordsOnceAsync(restClient, paymentAccountId);

                    if (condition(lastRecords))
                    {
                        return lastRecords;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                await Task.Delay(DefaultPollInterval, cancellationToken);
            }

            Assert.Fail(
                BuildTimeoutMessage(
                    $"Payment history for account '{paymentAccountId}'",
                    conditionDescription,
                    lastSnapshot,
                    lastException,
                    knownOperationIds));

            return lastRecords;
        }

        public static async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> WaitForHistoryRecordRemovedAsync(
            RestClient restClient,
            Guid paymentAccountId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            return await WaitForHistoryRecordsAsync(
                restClient,
                paymentAccountId,
                records => records.All(record => record.Record.Key != operationId),
                $"operation '{operationId}' is removed from payment history",
                [operationId],
                cancellationToken);
        }

        public static async Task<PaymentAccount> WaitForPaymentAccountAsync(
            RestClient restClient,
            Guid paymentAccountId,
            Func<PaymentAccount, bool> condition,
            string conditionDescription,
            IEnumerable<Guid> knownOperationIds = null,
            CancellationToken cancellationToken = default)
        {
            PaymentAccount lastAccount = null;
            ApiSnapshot lastSnapshot = null;
            Exception lastException = null;
            var timeoutAt = DateTime.UtcNow + DefaultTimeout;

            while (DateTime.UtcNow < timeoutAt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    (lastAccount, lastSnapshot) = await GetPaymentAccountOnceAsync(restClient, paymentAccountId);

                    if (lastAccount is not null && condition(lastAccount))
                    {
                        return lastAccount;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                await Task.Delay(DefaultPollInterval, cancellationToken);
            }

            Assert.Fail(
                BuildTimeoutMessage(
                    $"Payment account '{paymentAccountId}'",
                    conditionDescription,
                    lastSnapshot,
                    lastException,
                    knownOperationIds));

            return lastAccount;
        }

        private static async Task<(IReadOnlyCollection<PaymentOperationHistoryRecordResponse> Records, ApiSnapshot Snapshot)> GetHistoryRecordsOnceAsync(
            RestClient restClient,
            Guid paymentAccountId)
        {
            var request = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");
            var response = await restClient.ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(request);
            var snapshot = ApiSnapshot.From(response, BuildHistorySnapshot(response.Data?.Payload));

            return response.IsSuccessful && response.Data?.Payload != null
                ? (response.Data.Payload, snapshot)
                : (Array.Empty<PaymentOperationHistoryRecordResponse>(), snapshot);
        }

        private static async Task<(PaymentOperationHistoryRecordResponse Record, ApiSnapshot Snapshot)> GetHistoryRecordOnceAsync(
            RestClient restClient,
            Guid paymentAccountId,
            Guid operationId)
        {
            var request = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}/byId/{operationId}");
            var response = await restClient.ExecuteAsync<Result<PaymentOperationHistoryRecordResponse>>(request);
            var snapshot = ApiSnapshot.From(response, BuildHistorySnapshot(response.Data?.Payload is null ? [] : [response.Data.Payload]));

            return response.IsSuccessful && response.Data?.Payload != null
                ? (response.Data.Payload, snapshot)
                : (null, snapshot);
        }

        private static async Task<(PaymentAccount Account, ApiSnapshot Snapshot)> GetPaymentAccountOnceAsync(
            RestClient restClient,
            Guid paymentAccountId)
        {
            var request = new RestRequest($"{Endpoints.PaymentAccounts}/byId/{paymentAccountId}");
            var response = await restClient.ExecuteAsync<Result<PaymentAccount>>(request);
            var snapshot = ApiSnapshot.From(response, response.Data?.Payload is null
                ? "<missing>"
                : $"{response.Data.Payload.Key}:initial={response.Data.Payload.InitialBalance}:balance={response.Data.Payload.Balance}");

            return response.IsSuccessful && response.Data?.Payload != null
                ? (response.Data.Payload, snapshot)
                : (null, snapshot);
        }

        private static string BuildTimeoutMessage(
            string subject,
            string conditionDescription,
            ApiSnapshot lastSnapshot,
            Exception lastException,
            IEnumerable<Guid> knownOperationIds)
        {
            var operationIds = knownOperationIds?.Select(id => id.ToString()).ToArray() ?? [];

            return
                $"{subject} did not reach the expected state within {DefaultTimeout.TotalSeconds} seconds. " +
                $"Expected: {conditionDescription}. " +
                $"Known operation IDs: {(operationIds.Length == 0 ? "<none>" : string.Join(", ", operationIds))}. " +
                $"Mongo collections: history database collection prefix '<accountId>_yyyy_MM', ledger collection 'payment_accounts'. " +
                $"Last HTTP status: {lastSnapshot?.HttpStatus ?? "<none>"}. " +
                $"Last domain success: {lastSnapshot?.DomainSuccess?.ToString() ?? "<none>"}. " +
                $"Last domain status: {lastSnapshot?.DomainStatus ?? "<none>"}. " +
                $"Last snapshot: {lastSnapshot?.PayloadSnapshot ?? "<none>"}. " +
                $"Last response content: {lastSnapshot?.Content ?? "<none>"}. " +
                $"Last exception: {lastException?.Message ?? "<none>"} " +
                BuildInfrastructureDiagnostics(operationIds);
        }

        private static string BuildInfrastructureDiagnostics(IReadOnlyCollection<string> operationIds)
        {
            var testContainers = TestContainersService.GetInstance;
            if (testContainers is null || !testContainers.IsReadyForUse)
            {
                return "Infrastructure diagnostics: containers are not available.";
            }

            var diagnostics = new StringBuilder("Infrastructure diagnostics:");
            AppendSqlDiagnostics(diagnostics, testContainers, operationIds);
            AppendEventStoreDiagnostics(diagnostics, testContainers, operationIds);
            AppendMongoDiagnostics(diagnostics, testContainers, operationIds);

            return diagnostics.ToString();
        }

        private static void AppendSqlDiagnostics(
            StringBuilder diagnostics,
            TestContainersService testContainers,
            IReadOnlyCollection<string> operationIds)
        {
            try
            {
                using var connection = new SqlConnection(testContainers.AccountingDbConnectionString);
                connection.Open();

                diagnostics.Append(" SQL outbox rows=");
                diagnostics.Append(QueryOutboxRows(connection, operationIds));
                diagnostics.Append("; SQL inbox recent=");
                diagnostics.Append(QueryInboxRows(connection));
            }
            catch (Exception ex)
            {
                diagnostics.Append($" SQL diagnostics failed: {ex.Message};");
            }
        }

        private static string QueryOutboxRows(SqlConnection connection, IReadOnlyCollection<string> operationIds)
        {
            if (operationIds.Count == 0)
            {
                return "<no known operation ids>";
            }

            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT OperationId, EventType, Status, RetryCount, LastError, PublishedUtc, LockedUntilUtc
                FROM dbo.OutboxAccountPayments
                WHERE OperationId IN ({string.Join(", ", operationIds.Select((_, i) => $"@operationId{i}"))})
                ORDER BY CreatedUtc;";

            for (var i = 0; i < operationIds.Count; i++)
            {
                command.Parameters.AddWithValue($"@operationId{i}", operationIds.ElementAt(i));
            }

            using var reader = command.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
            {
                rows.Add(
                    $"{reader["OperationId"]}:{reader["EventType"]}:status={reader["Status"]}:retry={reader["RetryCount"]}:published={FormatDbValue(reader["PublishedUtc"])}:lockedUntil={FormatDbValue(reader["LockedUntilUtc"])}:error={FormatDbValue(reader["LastError"])}");
            }

            return rows.Count == 0 ? "<none>" : string.Join(" | ", rows);
        }

        private static string QueryInboxRows(SqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT TOP (10) MessageId, Status, RetryCount, LastError, ProcessedUtc
                FROM dbo.PaymentInboxMessages
                ORDER BY UpdatedUtc DESC;";

            using var reader = command.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
            {
                rows.Add(
                    $"{reader["MessageId"]}:status={reader["Status"]}:retry={reader["RetryCount"]}:processed={FormatDbValue(reader["ProcessedUtc"])}:error={FormatDbValue(reader["LastError"])}");
            }

            return rows.Count == 0 ? "<none>" : string.Join(" | ", rows);
        }

        private static void AppendEventStoreDiagnostics(
            StringBuilder diagnostics,
            TestContainersService testContainers,
            IReadOnlyCollection<string> operationIds)
        {
            try
            {
                if (operationIds.Count == 0)
                {
                    diagnostics.Append("; EventStore known operation events=<no known operation ids>");
                    return;
                }

                var settings = EventStoreClientSettings.Create(testContainers.EventSourceDbContainer.GetConnectionString());
                using var client = new EventStoreClient(settings);
                var matches = new List<string>();
                var operationIdSet = operationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var events = client.ReadAllAsync(Direction.Forwards, Position.Start);

                var readTask = Task.Run(async () =>
                {
                    await foreach (var resolvedEvent in events)
                    {
                        if (resolvedEvent.Event.EventType.StartsWith('$'))
                        {
                            continue;
                        }

                        var json = Encoding.UTF8.GetString(resolvedEvent.Event.Data.Span);
                        var matchedOperationId = operationIdSet.FirstOrDefault(json.Contains);
                        if (matchedOperationId != null)
                        {
                            matches.Add($"{matchedOperationId}:{resolvedEvent.Event.EventStreamId}:{resolvedEvent.Event.EventNumber}:{resolvedEvent.Event.EventType}");
                        }
                    }
                });

                if (!readTask.Wait(TimeSpan.FromSeconds(10)))
                {
                    diagnostics.Append("; EventStore diagnostics timed out");
                    return;
                }

                diagnostics.Append("; EventStore known operation events=");
                diagnostics.Append(matches.Count == 0 ? "<none>" : string.Join(" | ", matches));
            }
            catch (Exception ex)
            {
                diagnostics.Append($" EventStore diagnostics failed: {ex.Message};");
            }
        }

        private static void AppendMongoDiagnostics(
            StringBuilder diagnostics,
            TestContainersService testContainers,
            IReadOnlyCollection<string> operationIds)
        {
            try
            {
                var mongo = new MongoClient(testContainers.MongoDbContainer.GetConnectionString());
                var historyDb = mongo.GetDatabase("payments_history_test");
                var collectionNames = historyDb.ListCollectionNames().ToList();
                var operationFilterValues = operationIds
                    .Select(operationId => Guid.TryParse(operationId, out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .ToArray();

                diagnostics.Append("; Mongo history collections=");
                diagnostics.Append(collectionNames.Count == 0 ? "<none>" : string.Join(", ", collectionNames));

                if (operationFilterValues.Length == 0)
                {
                    return;
                }

                var operationRows = new List<string>();
                foreach (var collectionName in collectionNames)
                {
                    var collection = historyDb.GetCollection<BsonDocument>(collectionName);
                    var filter = Builders<BsonDocument>.Filter.In("Payload.Record.Key", operationFilterValues);
                    var count = collection.CountDocuments(filter);
                    if (count > 0)
                    {
                        operationRows.Add($"{collectionName}:{count}");
                    }
                }

                diagnostics.Append("; Mongo known operation rows=");
                diagnostics.Append(operationRows.Count == 0 ? "<none>" : string.Join(", ", operationRows));
            }
            catch (Exception ex)
            {
                diagnostics.Append($" Mongo diagnostics failed: {ex.Message};");
            }
        }

        private static string FormatDbValue(object value)
        {
            return value == null || value == DBNull.Value ? "<null>" : value.ToString();
        }

        private static string BuildHistorySnapshot(IEnumerable<PaymentOperationHistoryRecordResponse> records)
        {
            var snapshot = string.Join(
                " | ",
                (records ?? [])
                    .OrderBy(r => r.Record.OperationDay)
                    .ThenBy(r => r.Record.Key)
                    .Select(r => $"{r.Record.Key}:{r.Record.OperationDay:yyyy-MM-dd}:{r.Record.Amount}:{r.Balance}"));

            return string.IsNullOrWhiteSpace(snapshot) ? "<empty>" : snapshot;
        }

        private sealed class ApiSnapshot
        {
            public string HttpStatus { get; init; }
            public bool? DomainSuccess { get; init; }
            public string DomainStatus { get; init; }
            public string Content { get; init; }
            public string PayloadSnapshot { get; init; }

            public static ApiSnapshot From<T>(RestResponse<Result<T>> response, string payloadSnapshot)
            {
                if (response is null)
                {
                    return new ApiSnapshot
                    {
                        HttpStatus = "<null response>",
                        PayloadSnapshot = payloadSnapshot
                    };
                }

                return new ApiSnapshot
                {
                    HttpStatus = $"{(int)response.StatusCode} {response.StatusCode}, transport-success={response.IsSuccessful}, rest-error='{response.ErrorMessage}'",
                    DomainSuccess = response.Data?.IsSucceeded,
                    DomainStatus = response.Data?.StatusMessage,
                    Content = response.Content,
                    PayloadSnapshot = payloadSnapshot
                };
            }
        }
    }
}
