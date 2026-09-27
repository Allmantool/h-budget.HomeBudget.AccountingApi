using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [Order(IntegrationTestOrderIndex.MongoDocumentUniquenessTests)]
    public sealed class PaymentHistoryGenerationPublicationTests : BaseIntegrationTests
    {
        private IOptions<MongoDbOptions> _options;
        private MongoClient _mongoClient;
        private IMongoDatabase _database;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            var connectionString = TestContainers.MongoDbContainer.GetConnectionString();
            var databaseName = $"generation_publication_{Guid.NewGuid():N}";
            _options = Options.Create(new MongoDbOptions
            {
                ConnectionString = connectionString,
                PaymentsHistory = databaseName,
                LedgerDatabase = $"{databaseName}_ledger",
                PaymentAccounts = $"{databaseName}_accounts"
            });
            _mongoClient = new MongoClient(connectionString);
            _database = _mongoClient.GetDatabase(databaseName);
        }

        [SetUp]
        public async Task ResetAsync()
        {
            await _mongoClient.DropDatabaseAsync(_database.DatabaseNamespace.DatabaseName);
            await _mongoClient.DropDatabaseAsync(_options.Value.LedgerDatabase);
        }

        [OneTimeTearDown]
        public async Task CleanupAsync()
        {
            if (_database is not null)
            {
                await _mongoClient.DropDatabaseAsync(_database.DatabaseNamespace.DatabaseName);
                await _mongoClient.DropDatabaseAsync(_options.Value.LedgerDatabase);
            }
        }

        [Test]
        public async Task FiveIndependentClients_PublishSameNinetyEightRecordSnapshot_ExposeOneCompleteGeneration()
        {
            var accountId = Guid.NewGuid();
            var records = BuildRecords(accountId, 98, 10m);
            var snapshot = BuildSnapshot(accountId, records, 97);
            var clients = Enumerable.Range(0, 5)
                .Select(_ => new PaymentsHistoryDocumentsClient(_options))
                .ToArray();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = clients.Select(async client =>
            {
                await start.Task;
                return await client.PublishSnapshotAsync(snapshot, Guid.NewGuid(), CancellationToken.None);
            }).ToArray();
            start.SetResult();

            var publications = await Task.WhenAll(tasks);
            var visible = await clients[0].GetAsync(accountId, new DateOnly(2026, 1, 1).ToFinancialPeriod());

            publications.Should().ContainSingle(result => result.State == ProjectionPublicationState.Published);
            publications.All(result =>
                    result.State == ProjectionPublicationState.Published ||
                    result.State == ProjectionPublicationState.AlreadyPublished)
                .Should().BeTrue();
            visible.Should().HaveCount(98);
            visible.Select(document => document.Payload.Record.Key)
                .Should().BeEquivalentTo(records.Select(record => record.Record.Key));
            visible.Select(document => document.GenerationId).Distinct().Should().ContainSingle();

            foreach (var client in clients)
            {
                client.Dispose();
            }
        }

        [TestCase(2)]
        [TestCase(5)]
        public async Task IndependentProcesses_PublishSameSnapshot_ExposeOneCompleteGeneration(int processCount)
        {
            using var client = new PaymentsHistoryDocumentsClient(_options);
            var accountId = Guid.NewGuid();
            var gateFile = Path.Combine(Path.GetTempPath(), $"projection-publish-{Guid.NewGuid():N}.gate");
            var harnessRoot = Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "tools",
                    "PaymentHistoryProjectionPublisher"));
            var harness = new[] { "Release", "Debug" }
                .Select(configuration => Path.Combine(
                    harnessRoot,
                    "bin",
                    configuration,
                    "net10.0",
                    "PaymentHistoryProjectionPublisher.dll"))
                .FirstOrDefault(File.Exists);
            File.Exists(harness).Should().BeTrue("the process harness must be built with the integration project");
            var processes = Enumerable.Range(0, processCount)
                .Select(_ => StartPublisher(harness, accountId, gateFile))
                .ToArray();

            try
            {
                await File.WriteAllTextAsync(gateFile, "start");
                await Task.WhenAll(processes.Select(process => process.WaitForExitAsync()));
                var outputs = await Task.WhenAll(processes.Select(process => process.StandardOutput.ReadToEndAsync()));
                var errors = await Task.WhenAll(processes.Select(process => process.StandardError.ReadToEndAsync()));

                processes.Should().OnlyContain(
                    process => process.ExitCode == 0,
                    string.Join(Environment.NewLine, errors));
                outputs.Should().ContainSingle(output => output.StartsWith("Published|", StringComparison.Ordinal));
                outputs.Count(output => output.StartsWith("AlreadyPublished|", StringComparison.Ordinal))
                    .Should().Be(processCount - 1);
                var visible = await client.GetAsync(accountId, new DateOnly(2026, 1, 1).ToFinancialPeriod());
                visible.Should().HaveCount(98);
                visible.Select(document => document.GenerationId).Distinct().Should().ContainSingle();
                var ordered = visible.OrderBy(document => document.Payload.StreamRevision).ToArray();
                for (var index = 0; index < ordered.Length; index++)
                {
                    var record = ordered[index].Payload;
                    record.StreamRevision.Should().Be(index);
                    record.Balance.Should().Be(10m * (index + 1));
                    record.Record.Key.Should().Be(CreateProcessRecordId(accountId, index));
                    record.Record.PaymentAccountId.Should().Be(accountId);
                    record.Record.Amount.Should().Be(10m);
                    record.Record.OperationDay.Should().Be(new DateOnly(2026, 1, 1).AddDays(index % 28));
                    record.Record.Comment.Should().Be($"process-publication-{index}");
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }

                File.Delete(gateFile);
            }
        }

        [Test]
        public async Task OlderSnapshotCompletingAfterNewerSnapshot_CannotReplacePublishedHead()
        {
            using var firstClient = new PaymentsHistoryDocumentsClient(_options);
            using var secondClient = new PaymentsHistoryDocumentsClient(_options);
            var accountId = Guid.NewGuid();
            var older = BuildSnapshot(accountId, BuildRecords(accountId, 2, 10m), 1);
            var newerRecords = BuildRecords(accountId, 4, 20m);
            var newer = BuildSnapshot(accountId, newerRecords, 3);

            var newPublication = await firstClient.PublishSnapshotAsync(newer, Guid.NewGuid(), CancellationToken.None);
            var oldPublication = await secondClient.PublishSnapshotAsync(older, Guid.NewGuid(), CancellationToken.None);
            var visible = await firstClient.GetAsync(accountId, new DateOnly(2026, 1, 1).ToFinancialPeriod());

            newPublication.State.Should().Be(ProjectionPublicationState.Published);
            oldPublication.State.Should().Be(ProjectionPublicationState.Superseded);
            oldPublication.CommittedRevision.Should().Be(3);
            visible.Select(document => document.Payload.Record.Key)
                .Should().BeEquivalentTo(newerRecords.Select(record => record.Record.Key));
            visible.Max(document => document.Payload.Balance).Should().Be(80m);
        }

        [Test]
        public async Task OlderBalanceFence_CannotOverwriteNewerAccountBalance()
        {
            using var accountClient = new PaymentAccountDocumentClient(_options);
            var accountId = Guid.NewGuid();
            await accountClient.InsertOneAsync(new PaymentAccount
            {
                Key = accountId,
                InitialBalance = 25m,
                Balance = 25m
            });

            var newer = await accountClient.UpdateBalanceIfNewerAsync(
                accountId,
                225m,
                projectionFence: 2,
                CancellationToken.None);
            var older = await accountClient.UpdateBalanceIfNewerAsync(
                accountId,
                125m,
                projectionFence: 1,
                CancellationToken.None);
            var stored = await accountClient.GetByIdAsync(accountId.ToString());

            newer.IsSucceeded.Should().BeTrue();
            older.IsSucceeded.Should().BeTrue("a superseded writer is already durably satisfied");
            stored.Payload.Payload.Balance.Should().Be(225m);
            stored.Payload.PaymentHistoryProjectionFence.Should().Be(2);
        }

        [Test]
        public async Task LegacyUnfencedBalanceUpdate_ChangesBalanceWithoutReplacingAccountMetadata()
        {
            using var accountClient = new PaymentAccountDocumentClient(_options);
            var accountId = Guid.NewGuid();
            await accountClient.InsertOneAsync(new PaymentAccount
            {
                Key = accountId,
                InitialBalance = 25m,
                Balance = 25m,
                Description = "preserved"
            });

            var result = await accountClient.UpdateBalanceAsync(
                accountId,
                125m,
                CancellationToken.None);
            var stored = await accountClient.GetByIdAsync(accountId.ToString());

            result.IsSucceeded.Should().BeTrue();
            stored.Payload.Payload.Balance.Should().Be(125m);
            stored.Payload.Payload.InitialBalance.Should().Be(25m);
            stored.Payload.Payload.Description.Should().Be("preserved");
        }

        [Test]
        public async Task AccountMetadataUpdate_ConcurrentWithProjection_PreservesNewerBalanceAndFence()
        {
            using var accountClient = new PaymentAccountDocumentClient(_options);
            using var projectionClient = new PaymentAccountDocumentClient(_options);
            var accountId = Guid.NewGuid();
            var account = new PaymentAccount
            {
                Key = accountId,
                InitialBalance = 25m,
                Balance = 25m,
                Description = "before"
            };
            await accountClient.InsertOneAsync(account);
            account.Description = "after";
            account.Balance = -999m;

            await Task.WhenAll(
                accountClient.UpdateAsync(accountId.ToString(), account),
                projectionClient.UpdateBalanceIfNewerAsync(
                    accountId,
                    225m,
                    projectionFence: 2,
                    CancellationToken.None));
            await projectionClient.UpdateBalanceIfNewerAsync(
                accountId,
                125m,
                projectionFence: 1,
                CancellationToken.None);
            var stored = await accountClient.GetByIdAsync(accountId.ToString());

            stored.Payload.Payload.Description.Should().Be("after");
            stored.Payload.Payload.Balance.Should().Be(225m);
            stored.Payload.PaymentHistoryProjectionFence.Should().Be(2);
        }

        [Test]
        public async Task DifferentPeriodBalanceSnapshots_UseAccountFenceToRejectStaleTotal()
        {
            using var historyClient = new PaymentsHistoryDocumentsClient(_options);
            using var accountClient = new PaymentAccountDocumentClient(_options);
            using var competingAccountClient = new PaymentAccountDocumentClient(_options);
            var accountId = Guid.NewGuid();
            await accountClient.InsertOneAsync(new PaymentAccount
            {
                Key = accountId,
                InitialBalance = 25m,
                Balance = 25m
            });
            var january = BuildSnapshot(accountId, BuildRecords(accountId, 2, 10m, 1), 1);
            var february = BuildSnapshot(accountId, BuildRecords(accountId, 3, 5m, 2), 2);

            await historyClient.PublishSnapshotAsync(january, Guid.NewGuid(), CancellationToken.None);
            var olderTotal = await historyClient.CreateAccountBalanceSnapshotAsync(
                accountId,
                CancellationToken.None);
            await historyClient.PublishSnapshotAsync(february, Guid.NewGuid(), CancellationToken.None);
            var newerTotal = await historyClient.CreateAccountBalanceSnapshotAsync(
                accountId,
                CancellationToken.None);
            await Task.WhenAll(
                accountClient.UpdateBalanceIfNewerAsync(
                    accountId,
                    25m + newerTotal.ProjectedBalance,
                    newerTotal.Fence,
                    CancellationToken.None),
                competingAccountClient.UpdateBalanceIfNewerAsync(
                    accountId,
                    25m + olderTotal.ProjectedBalance,
                    olderTotal.Fence,
                    CancellationToken.None));
            var stored = await accountClient.GetByIdAsync(accountId.ToString());

            olderTotal.ProjectedBalance.Should().Be(20m);
            newerTotal.ProjectedBalance.Should().Be(35m);
            newerTotal.Fence.Should().BeGreaterThan(olderTotal.Fence);
            stored.Payload.Payload.Balance.Should().Be(60m);
            stored.Payload.PaymentHistoryProjectionFence.Should().Be(newerTotal.Fence);
        }

        [Test]
        public async Task ReadersAndPagination_ExcludeInactiveGenerations()
        {
            using var client = new PaymentsHistoryDocumentsClient(_options);
            var accountId = Guid.NewGuid();
            var records = BuildRecords(accountId, 5, 10m);
            var snapshot = BuildSnapshot(accountId, records, 4);
            await client.PublishSnapshotAsync(snapshot, Guid.NewGuid(), CancellationToken.None);
            var hiddenOperationId = Guid.NewGuid();
            await _database.GetCollection<PaymentHistoryDocument>("_payment_history_generations")
                .InsertOneAsync(new PaymentHistoryDocument
                {
                    GenerationId = "OBSOLETE-GENERATION",
                    ProjectionRunId = Guid.NewGuid(),
                    Payload = new PaymentOperationHistoryRecord
                    {
                        Balance = 999m,
                        StreamRevision = 99,
                        Record = new FinancialTransaction
                        {
                            Key = hiddenOperationId,
                            PaymentAccountId = accountId,
                            OperationDay = new DateOnly(2026, 1, 15),
                            Amount = 999m
                        }
                    }
                });

            var history = await client.GetAsync(accountId);
            var byId = await client.GetByIdAsync(accountId, hiddenOperationId);
            var page = await client.QueryAsync(
                accountId,
                new PaymentHistoryQuery { Page = 1, PageSize = 2 },
                CancellationToken.None);

            history.Should().HaveCount(5);
            history.Should().NotContain(document => document.Payload.Record.Key == hiddenOperationId);
            byId.Should().BeNull();
            page.TotalCount.Should().Be(5);
            page.Items.Should().HaveCount(2);
            page.Items.Should().NotContain(document => document.Payload.Record.Key == hiddenOperationId);
        }

        [Test]
        public async Task CompleteEmptySnapshot_PublishesAnIntentionallyEmptyPeriod()
        {
            using var client = new PaymentsHistoryDocumentsClient(_options);
            var accountId = Guid.NewGuid();
            var original = BuildSnapshot(accountId, BuildRecords(accountId, 2, 10m), 1);
            await client.PublishSnapshotAsync(original, Guid.NewGuid(), CancellationToken.None);
            var empty = BuildSnapshot(accountId, [], 2, month: 1);

            var result = await client.PublishSnapshotAsync(empty, Guid.NewGuid(), CancellationToken.None);
            var visible = await client.GetAsync(accountId, new DateOnly(2026, 1, 1).ToFinancialPeriod());

            result.State.Should().Be(ProjectionPublicationState.Published);
            visible.Should().BeEmpty();
        }

        [Test]
        public async Task ScopeDiscovery_DoesNotCreateCollectionsOrIndexes()
        {
            using var client = new PaymentsHistoryDocumentsClient(_options);
            var accountId = Guid.NewGuid();
            var period = new DateOnly(2026, 1, 1)
                .ToFinancialPeriod()
                .ToFinancialMonthIdentifier(accountId);
            await _database.GetCollection<PaymentHistoryDocument>(period).InsertOneAsync(
                new PaymentHistoryDocument
                {
                    Payload = BuildRecords(accountId, 1, 10m).Single()
                });
            var before = await (await _database.ListCollectionNamesAsync()).ToListAsync();

            var scopes = await client.DiscoverProjectionScopesAsync(CancellationToken.None);
            var after = await (await _database.ListCollectionNamesAsync()).ToListAsync();

            scopes.Should().ContainSingle(scope =>
                scope.PaymentAccountId == accountId &&
                scope.FinancialPeriodIdentifier == period);
            after.Should().BeEquivalentTo(before);
            after.Should().NotContain("_payment_history_projection_heads");
        }

        [Test]
        public async Task IncompleteOrCorruptStagedGeneration_IsNeverVisibleAndCannotMoveHead()
        {
            using var client = new PaymentsHistoryDocumentsClient(_options);
            var accountId = Guid.NewGuid();
            var records = BuildRecords(accountId, 3, 5m);
            var snapshot = BuildSnapshot(accountId, records, 2);
            var generationId = CreateGenerationId(snapshot);
            var corrupt = BuildRecords(accountId, 1, 999m).Single();
            var generations = _database.GetCollection<PaymentHistoryDocument>("_payment_history_generations");
            await generations.InsertOneAsync(new PaymentHistoryDocument
            {
                GenerationId = generationId,
                ProjectionRunId = Guid.NewGuid(),
                Payload = corrupt
            });

            var publish = async () => await client.PublishSnapshotAsync(
                snapshot,
                Guid.NewGuid(),
                CancellationToken.None);

            await publish.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*incomplete or does not match*");
            var visible = await client.GetAsync(accountId, new DateOnly(2026, 1, 1).ToFinancialPeriod());
            visible.Should().BeEmpty();
            var headCount = await _database
                .GetCollection<PaymentHistoryProjectionHeadDocument>("_payment_history_projection_heads")
                .CountDocumentsAsync(FilterDefinition<PaymentHistoryProjectionHeadDocument>.Empty);
            headCount.Should().Be(0);
        }

        [Test]
        public async Task CorrectedRebuild_AtomicallyReplacesDamagedLegacyProjection_AndIsIdempotent()
        {
            using var client = new PaymentsHistoryDocumentsClient(_options);
            var accountId = Guid.NewGuid();
            var records = BuildRecords(accountId, 98, 7m);
            var snapshot = BuildSnapshot(accountId, records, 97);
            var legacy = _database.GetCollection<PaymentHistoryDocument>(snapshot.FinancialPeriodIdentifier);
            await legacy.InsertManyAsync(records.Take(75).Select(record => new PaymentHistoryDocument
            {
                ProjectionRunId = Guid.NewGuid(),
                Payload = record
            }));

            var before = await client.GetAsync(accountId, new DateOnly(2026, 1, 1).ToFinancialPeriod());
            var first = await client.PublishSnapshotAsync(snapshot, Guid.NewGuid(), CancellationToken.None);
            var second = await client.PublishSnapshotAsync(snapshot, Guid.NewGuid(), CancellationToken.None);
            var after = await client.GetAsync(accountId, new DateOnly(2026, 1, 1).ToFinancialPeriod());

            before.Should().HaveCount(75);
            first.State.Should().Be(ProjectionPublicationState.Published);
            second.State.Should().Be(ProjectionPublicationState.AlreadyPublished);
            after.Should().HaveCount(98);
            after.Select(document => document.Payload.Record.Key)
                .Should().BeEquivalentTo(records.Select(record => record.Record.Key));
            after.Max(document => document.Payload.Balance).Should().Be(686m);
            (await legacy.CountDocumentsAsync(FilterDefinition<PaymentHistoryDocument>.Empty)).Should().Be(
                75,
                "publication must not rewrite or delete the legacy evidence collection");
        }

        private static PaymentHistoryProjectionSnapshot BuildSnapshot(
            Guid accountId,
            IReadOnlyList<PaymentOperationHistoryRecord> records,
            long revision,
            int month = 0)
        {
            var operationMonth = month == 0 && records.Count > 0
                ? records[0].Record.OperationDay.Month
                : Math.Max(month, 1);
            var period = new DateOnly(2026, operationMonth, 1)
                .ToFinancialPeriod()
                .ToFinancialMonthIdentifier(accountId);
            return new PaymentHistoryProjectionSnapshot(
                period,
                accountId,
                $"payment-account-{accountId:D}-2026-{operationMonth:00}",
                revision,
                null,
                PaymentHistoryProjectionFingerprint.Create(records),
                records);
        }

        private static IReadOnlyList<PaymentOperationHistoryRecord> BuildRecords(
            Guid accountId,
            int count,
            decimal amount,
            int month = 1)
        {
            return Enumerable.Range(0, count)
                .Select(index => new PaymentOperationHistoryRecord
                {
                    Balance = amount * (index + 1),
                    StreamRevision = index,
                    Record = new FinancialTransaction
                    {
                        Key = Guid.NewGuid(),
                        OperationUnixTime = 1767225600000L + index,
                        PaymentAccountId = accountId,
                        Amount = amount,
                        OperationDay = new DateOnly(2026, month, 1).AddDays(index % 28),
                        Comment = $"generation-test-{index}"
                    }
                })
                .ToArray();
        }

        private static string CreateGenerationId(PaymentHistoryProjectionSnapshot snapshot)
        {
            var value = string.Join("|", snapshot.StreamId, snapshot.SourceRevision, snapshot.SnapshotHash);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)));
        }

        private static Guid CreateProcessRecordId(Guid accountId, int index)
        {
            var bytes = accountId.ToByteArray();
            var indexBytes = BitConverter.GetBytes(index);
            for (var position = 0; position < indexBytes.Length; position++)
            {
                bytes[position] ^= indexBytes[position];
            }

            return new Guid(bytes);
        }

        private Process StartPublisher(string harness, Guid accountId, string gateFile)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(harness);
            startInfo.ArgumentList.Add(TestContainers.MongoDbContainer.GetConnectionString());
            startInfo.ArgumentList.Add(_database.DatabaseNamespace.DatabaseName);
            startInfo.ArgumentList.Add(accountId.ToString("D"));
            startInfo.ArgumentList.Add("98");
            startInfo.ArgumentList.Add("97");
            startInfo.ArgumentList.Add(gateFile);
            startInfo.ArgumentList.Add(Guid.NewGuid().ToString("D"));
            return Process.Start(startInfo);
        }
    }
}
