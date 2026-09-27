using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Workers.OperationsConsumer.Clients;
using HomeBudget.Accounting.Workers.OperationsConsumer.Models;
using HomeBudget.Components.Accounts.Clients;
using HomeBudget.Components.Categories.Clients;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [Order(IntegrationTestOrderIndex.PaymentOperationsEventStoreClientTests)]
    public sealed class ProjectionWorkerProcessConcurrencyTests : BaseIntegrationTests
    {
        [TestCase(2)]
        [TestCase(5)]
        public async Task IndependentWorkerProcesses_RebuildSameStream_ExposeOneExactGeneration(
            int processCount)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var historyDatabase = $"worker_process_history_{suffix}";
            var handbookDatabase = $"worker_process_handbooks_{suffix}";
            var ledgerDatabase = $"worker_process_ledger_{suffix}";
            var mongoConnection = TestContainers.MongoDbContainer.GetConnectionString();
            var eventStoreConnection = TestContainers.EventSourceDbContainer.GetConnectionString();
            var options = Options.Create(new MongoDbOptions
            {
                ConnectionString = mongoConnection,
                PaymentsHistory = historyDatabase,
                HandBooks = handbookDatabase,
                LedgerDatabase = ledgerDatabase,
                PaymentAccounts = $"worker_process_accounts_{suffix}"
            });
            var accountId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var operationDay = new DateOnly(2026, 4, 1);
            var period = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(accountId);
            var streamId = PaymentOperationNamesGenerator.GenerateForAccountMonthStream(period);
            var events = BuildEvents(accountId, categoryId, operationDay);
            using var eventStoreClient = new EventStoreClient(
                EventStoreClientSettings.Create(eventStoreConnection));
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
                Moq.Mock.Of<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
                Moq.Mock.Of<HomeBudget.Accounting.Infrastructure.Providers.Interfaces.IDateTimeProvider>(),
                eventStoreClient,
                eventStoreOptions);
            using var accounts = new PaymentAccountDocumentClient(options);
            using var categories = new CategoryDocumentsClient(options);
            using var history = new PaymentsHistoryDocumentsClient(options);
            await accounts.InsertOneAsync(new PaymentAccount
            {
                Key = accountId,
                InitialBalance = 50m,
                Balance = 50m,
                Currency = "BYN"
            });
            await categories.InsertOneAsync(new Category(CategoryTypes.Income, ["worker-process"])
            {
                Key = categoryId
            });
            await writer.SendBatchAsync(events, streamId);
            var authoritativeBefore = await reader.ReadAsync(streamId).ToListAsync();
            var planPath = Path.Combine(Path.GetTempPath(), $"projection-worker-plan-{suffix}.json");
            var plan = new ProjectionRebuildPlan(
                "disposable-process-smoke",
                new MongoUrl(mongoConnection).Server.ToString(),
                historyDatabase,
                new Uri(eventStoreConnection).Authority,
                DateTime.UtcNow,
                [new ProjectionRebuildPlanItem(accountId, period, streamId, 97, 98, null)]);
            await File.WriteAllTextAsync(
                planPath,
                JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await using var hangingEndpoint = new HangingHttpEndpoint();
            var victim = StartWorker(
                planPath,
                mongoConnection,
                historyDatabase,
                handbookDatabase,
                ledgerDatabase,
                eventStoreConnection,
                hangingEndpoint.Url);
            var victimOutput = victim.StandardOutput.ReadToEndAsync();
            var victimError = victim.StandardError.ReadToEndAsync();
            var mongo = new MongoClient(mongoConnection);
            var auditCollection = mongo.GetDatabase(historyDatabase)
                .GetCollection<ProjectionAuditDocument>("_projection_audit");
            await WaitForPublishedAuditAsync(auditCollection, streamId);
            victim.Kill(entireProcessTree: true);
            await victim.WaitForExitAsync();
            await Task.WhenAll(victimOutput, victimError);
            victim.Dispose();

            var processes = Enumerable.Range(0, processCount)
                .Select(_ => StartWorker(
                    planPath,
                    mongoConnection,
                    historyDatabase,
                    handbookDatabase,
                    ledgerDatabase,
                    eventStoreConnection,
                    hangingEndpoint.Url))
                .ToArray();

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var outputTasks = processes
                    .Select(process => process.StandardOutput.ReadToEndAsync(timeout.Token))
                    .ToArray();
                var errorTasks = processes
                    .Select(process => process.StandardError.ReadToEndAsync(timeout.Token))
                    .ToArray();
                await Task.WhenAll(processes.Select(process => process.WaitForExitAsync(timeout.Token)));
                var standardOutput = await Task.WhenAll(outputTasks);
                var standardError = await Task.WhenAll(errorTasks);
                processes.Should().OnlyContain(
                    process => process.ExitCode == 0,
                    string.Join(Environment.NewLine, standardOutput.Concat(standardError)));

                var visible = (await history.GetAsync(accountId, operationDay.ToFinancialPeriod()))
                    .OrderBy(document => document.Payload.StreamRevision)
                    .ToArray();
                var expectedBalances = authoritativeBefore
                    .OrderBy(paymentEvent => paymentEvent.Payload.OperationDay)
                    .ThenBy(paymentEvent => paymentEvent.Payload.OperationUnixTime)
                    .ThenBy(paymentEvent => paymentEvent.SequenceNumber)
                    .ThenBy(paymentEvent => paymentEvent.Payload.Key)
                    .Select((paymentEvent, index) => new
                    {
                        paymentEvent.Payload.Key,
                        Balance = 10m * (index + 1)
                    })
                    .ToDictionary(item => item.Key, item => item.Balance);
                visible.Should().HaveCount(98);
                visible.Select(document => document.GenerationId).Distinct().Should().ContainSingle();
                for (var index = 0; index < visible.Length; index++)
                {
                    var projected = visible[index].Payload;
                    var source = authoritativeBefore[index];
                    projected.StreamRevision.Should().Be(index);
                    projected.Record.Key.Should().Be(source.Payload.Key);
                    projected.Record.PaymentAccountId.Should().Be(source.Payload.PaymentAccountId);
                    projected.Record.CategoryId.Should().Be(source.Payload.CategoryId);
                    projected.Record.Amount.Should().Be(source.Payload.Amount);
                    projected.Record.OperationDay.Should().Be(source.Payload.OperationDay);
                    projected.Record.OperationUnixTime.Should().Be(source.Payload.OperationUnixTime);
                    projected.Record.Comment.Should().Be(source.Payload.Comment);
                    PayloadHash(projected.Record).Should().Be(PayloadHash(source.Payload));
                    projected.Balance.Should().Be(expectedBalances[projected.Record.Key]);
                }

                var storedAccount = await accounts.GetByIdAsync(accountId.ToString());
                storedAccount.Payload.Payload.Balance.Should().Be(1030m);
                storedAccount.Payload.PaymentHistoryProjectionFence.Should().BePositive();
                var audits = await auditCollection
                    .Find(document => document.Payload.StreamId == streamId)
                    .ToListAsync();
                audits.Should().HaveCount(processCount + 1);
                audits.Count(document => document.Payload.Status == "Succeeded").Should().Be(processCount);
                audits.Should().ContainSingle(document =>
                    document.Payload.Status == "Published" &&
                    document.Payload.CompletedUtc == null);
                audits.Count(document => document.Payload.PublicationState == "Published").Should().Be(1);
                audits.Count(document => document.Payload.PublicationState == "AlreadyPublished")
                    .Should().Be(processCount);
                var authoritativeAfter = await reader.ReadAsync(streamId).ToListAsync();
                EventEvidence(authoritativeAfter).Should().Equal(EventEvidence(authoritativeBefore));
            }
            finally
            {
                foreach (var process in processes)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    process.Dispose();
                }

                File.Delete(planPath);
            }
        }

        private static PaymentOperationEvent[] BuildEvents(
            Guid accountId,
            Guid categoryId,
            DateOnly operationDay)
        {
            return Enumerable.Range(0, 98)
                .Select(index => new PaymentOperationEvent
                {
                    EnvelopId = Guid.NewGuid(),
                    EventType = PaymentEventTypes.Added,
                    Payload = new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        OperationUnixTime = 1775001600000L + index,
                        PaymentAccountId = accountId,
                        CategoryId = categoryId,
                        ContractorId = CreateDeterministicGuid(accountId, index + 1000),
                        TransactionType = TransactionTypes.Payment,
                        ConversionMultiplier = 1.25m,
                        ScopedOperationId = index + 1,
                        OperationDay = operationDay.AddDays(index % 28),
                        Amount = 10m,
                        Comment = $"worker-process-{index}"
                    }
                })
                .ToArray();
        }

        private Process StartWorker(
            string planPath,
            string mongoConnection,
            string historyDatabase,
            string handbookDatabase,
            string ledgerDatabase,
            string eventStoreConnection,
            string notificationEndpoint)
        {
            var outputDirectory = Path.GetDirectoryName(
                typeof(HomeBudget.Accounting.Workers.OperationsConsumer.Program).Assembly.Location);
            var worker = Path.Combine(
                outputDirectory,
                "HomeBudget.Accounting.Workers.OperationsConsumer.dll");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = outputDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(worker);
            start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
            start.Environment["MongoDbOptions__ConnectionString"] = mongoConnection;
            start.Environment["MongoDbOptions__PaymentsHistory"] = historyDatabase;
            start.Environment["MongoDbOptions__HandBooks"] = handbookDatabase;
            start.Environment["MongoDbOptions__LedgerDatabase"] = ledgerDatabase;
            start.Environment["EventStoreDb__Url"] = eventStoreConnection;
            start.Environment["DatabaseConnectionOptions__ConnectionString"] =
                TestContainers.AccountingDbConnectionString;
            start.Environment["NotificationPublisherOptions__AccountingApiBaseUrl"] = notificationEndpoint;
            start.Environment["ElasticSearchOptions__IsEnabled"] = "false";
            start.Environment["SeqOptions__IsEnabled"] = "false";
            start.Environment["ProjectionRebuild__Enabled"] = "true";
            start.Environment["ProjectionRebuild__Execute"] = "true";
            start.Environment["ProjectionRebuild__OldWritersStopped"] = "true";
            start.Environment["ProjectionRebuild__ExpectedTarget"] = "disposable-process-smoke";
            start.Environment["ProjectionRebuild__ConfirmTarget"] = "disposable-process-smoke";
            start.Environment["ProjectionRebuild__ScopeFile"] = planPath;
            start.Environment["ProjectionRebuild__MaxScopes"] = "1";
            return Process.Start(start);
        }

        private static async Task WaitForPublishedAuditAsync(
            IMongoCollection<ProjectionAuditDocument> audits,
            string streamId)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!timeout.IsCancellationRequested)
            {
                var published = await audits.Find(document =>
                        document.Payload.StreamId == streamId &&
                        document.Payload.Status == "Published")
                    .AnyAsync(timeout.Token);
                if (published)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }

            throw new TimeoutException("The victim worker did not publish its projection before the restart deadline.");
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
                PayloadHash = PayloadHash(paymentEvent.Payload)
            }).ToArray();
        }

        private static string PayloadHash(FinancialTransaction transaction) => Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(transaction)));

        private static Guid CreateDeterministicGuid(Guid seed, int value)
        {
            var bytes = seed.ToByteArray();
            var valueBytes = BitConverter.GetBytes(value);
            for (var index = 0; index < valueBytes.Length; index++)
            {
                bytes[index] ^= valueBytes[index];
            }

            return new Guid(bytes);
        }

        private sealed class HangingHttpEndpoint : IAsyncDisposable
        {
            private readonly CancellationTokenSource _stopping = new();
            private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
            private readonly Task _acceptLoop;

            public HangingHttpEndpoint()
            {
                _listener.Start();
                var endpoint = (IPEndPoint)_listener.LocalEndpoint;
                Url = $"http://127.0.0.1:{endpoint.Port}";
                _acceptLoop = AcceptAsync();
            }

            public string Url { get; }

            public async ValueTask DisposeAsync()
            {
                _stopping.Cancel();
                _listener.Stop();
                try
                {
                    await _acceptLoop;
                }
                catch (OperationCanceledException)
                {
                }
                catch (SocketException)
                {
                }

                _stopping.Dispose();
            }

            private async Task AcceptAsync()
            {
                while (!_stopping.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    _ = HoldConnectionAsync(client, _stopping.Token);
                }
            }

            private static async Task HoldConnectionAsync(
                TcpClient client,
                CancellationToken cancellationToken)
            {
                using (client)
                {
                    try
                    {
                        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }
    }
}
