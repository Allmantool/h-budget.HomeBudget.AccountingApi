using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Accounts.Clients;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Components.Categories.Clients;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Components.Contractors.Clients;
using HomeBudget.Components.Contractors.Models;
using HomeBudget.Components.Operations.Clients;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.Clients
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [Order(IntegrationTestOrderIndex.MongoDocumentUniquenessTests)]
    public class MongoDocumentUniquenessTests : BaseIntegrationTests
    {
        private IOptions<MongoDbOptions> _mongoOptions;
        private MongoClient _mongoClient;
        private IMongoDatabase _handbooksDatabase;
        private IMongoDatabase _ledgerDatabase;
        private IMongoDatabase _historyDatabase;

        private CategoryDocumentsClient _categoryClient;
        private ContractorDocumentsClient _contractorClient;
        private PaymentAccountDocumentClient _paymentAccountClient;
        private PaymentsHistoryDocumentsClient _historyClient;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();

            var dbConnection = TestContainers.MongoDbContainer.GetConnectionString();
            _mongoOptions = Options.Create(new MongoDbOptions
            {
                ConnectionString = dbConnection,
                LedgerDatabase = "uniqueness_ledger_test",
                PaymentsHistory = "uniqueness_history_test",
                HandBooks = "uniqueness_handbooks_test",
                PaymentAccounts = "uniqueness_payment_accounts_test"
            });

            _mongoClient = new MongoClient(dbConnection);
            _handbooksDatabase = _mongoClient.GetDatabase(_mongoOptions.Value.HandBooks);
            _ledgerDatabase = _mongoClient.GetDatabase(_mongoOptions.Value.LedgerDatabase);
            _historyDatabase = _mongoClient.GetDatabase(_mongoOptions.Value.PaymentsHistory);

            _categoryClient = new CategoryDocumentsClient(_mongoOptions);
            _contractorClient = new ContractorDocumentsClient(_mongoOptions);
            _paymentAccountClient = new PaymentAccountDocumentClient(_mongoOptions);
            _historyClient = new PaymentsHistoryDocumentsClient(_mongoOptions);
        }

        [SetUp]
        public async Task TestSetupAsync()
        {
            await _mongoClient.DropDatabaseAsync(_mongoOptions.Value.HandBooks);
            await _mongoClient.DropDatabaseAsync(_mongoOptions.Value.LedgerDatabase);
            await _mongoClient.DropDatabaseAsync(_mongoOptions.Value.PaymentsHistory);
        }

        [Test]
        public async Task ConcurrentCategoryCreate_WithSameBusinessKey_ShouldFailDuplicatesAndStoreOneDocument()
        {
            await _categoryClient.GetAsync();
            var tasks = Enumerable.Range(0, 20)
                .Select(_ => _categoryClient.InsertOneAsync(new Category(CategoryTypes.Income, ["concurrent-category"])
                {
                    Key = Guid.NewGuid()
                }));

            var results = await Task.WhenAll(tasks);

            var collection = _handbooksDatabase.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);
            var stored = await collection.Find(c => c.Payload.CategoryKey == "0-concurrent-category").ToListAsync();

            results.Count(result => result.IsSucceeded).Should().Be(1);
            results.Where(result => !result.IsSucceeded).Should().OnlyContain(
                result => result.StatusMessage == "The category with '0-concurrent-category' key already exists");
            results.Where(result => result.IsSucceeded).Select(result => result.Payload).Should().ContainSingle();
            stored.Should().ContainSingle();
        }

        [Test]
        public async Task ConcurrentContractorCreate_WithSameBusinessKey_ShouldFailDuplicatesAndStoreOneDocument()
        {
            await _contractorClient.GetAsync();
            var tasks = Enumerable.Range(0, 20)
                .Select(_ => _contractorClient.InsertOneAsync(new Contractor(["concurrent-contractor"])
                {
                    Key = Guid.NewGuid()
                }));

            var results = await Task.WhenAll(tasks);

            var collection = _handbooksDatabase.GetCollection<ContractorDocument>(LedgerDbCollections.Contractors);
            var stored = await collection.Find(c => c.Payload.ContractorKey == "concurrent-contractor").ToListAsync();

            results.Count(result => result.IsSucceeded).Should().Be(1);
            results.Where(result => !result.IsSucceeded).Should().OnlyContain(
                result => result.StatusMessage == "The contractor with 'concurrent-contractor' key already exists");
            results.Where(result => result.IsSucceeded).Select(result => result.Payload).Should().ContainSingle();
            stored.Should().ContainSingle();
        }

        [Test]
        public async Task DuplicatePaymentAccountCreate_WithSameKey_ShouldReturnExistingAndStoreOneDocument()
        {
            await _paymentAccountClient.GetAsync();
            var accountId = Guid.NewGuid();
            var first = CreatePaymentAccount(accountId, "first account");
            var second = CreatePaymentAccount(accountId, "second account");

            var results = await Task.WhenAll(
                _paymentAccountClient.InsertOneAsync(first),
                _paymentAccountClient.InsertOneAsync(second));

            var collection = _ledgerDatabase.GetCollection<PaymentAccountDocument>(LedgerDbCollections.PaymentAccounts);
            var stored = await collection.Find(c => c.Payload.Key == accountId).ToListAsync();

            results.Should().OnlyContain(result => result.IsSucceeded);
            results.Select(result => result.Payload).Should().OnlyContain(id => id == accountId);
            stored.Should().ContainSingle();
        }

        [Test]
        public async Task HandbookMigrationRerun_WithSameBusinessKeys_ShouldNotDuplicateDocuments()
        {
            var firstCategory = new Category(CategoryTypes.Expense, ["migration", "category"]) { Key = Guid.NewGuid() };
            var secondCategory = new Category(CategoryTypes.Expense, ["migration", "category"]) { Key = Guid.NewGuid() };
            var firstContractor = new Contractor(["migration", "contractor"]) { Key = Guid.NewGuid() };
            var secondContractor = new Contractor(["migration", "contractor"]) { Key = Guid.NewGuid() };

            var firstCategoryResult = await _categoryClient.InsertOneAsync(firstCategory);
            var secondCategoryResult = await _categoryClient.InsertOneAsync(secondCategory);
            var firstContractorResult = await _contractorClient.InsertOneAsync(firstContractor);
            var secondContractorResult = await _contractorClient.InsertOneAsync(secondContractor);

            var categories = await _handbooksDatabase.GetCollection<CategoryDocument>(LedgerDbCollections.Categories)
                .Find(c => c.Payload.CategoryKey == firstCategory.CategoryKey)
                .ToListAsync();
            var contractors = await _handbooksDatabase.GetCollection<ContractorDocument>(LedgerDbCollections.Contractors)
                .Find(c => c.Payload.ContractorKey == firstContractor.ContractorKey)
                .ToListAsync();

            firstCategoryResult.IsSucceeded.Should().BeTrue();
            secondCategoryResult.IsSucceeded.Should().BeFalse();
            secondCategoryResult.StatusMessage.Should().Be(
                $"The category with '{firstCategory.CategoryKey}' key already exists");
            firstContractorResult.IsSucceeded.Should().BeTrue();
            secondContractorResult.IsSucceeded.Should().BeFalse();
            secondContractorResult.StatusMessage.Should().Be(
                $"The contractor with '{firstContractor.ContractorKey}' key already exists");
            categories.Should().ContainSingle();
            contractors.Should().ContainSingle();
        }

        [Test]
        public async Task UniqueIndexes_ShouldBeCreatedForHandbooksAccountsAndHistoryProjection()
        {
            await _categoryClient.InsertOneAsync(new Category(CategoryTypes.Income, ["indexed-category"]) { Key = Guid.NewGuid() });
            await _contractorClient.InsertOneAsync(new Contractor(["indexed-contractor"]) { Key = Guid.NewGuid() });
            await _paymentAccountClient.InsertOneAsync(CreatePaymentAccount(Guid.NewGuid(), "indexed account"));

            var operationId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            const string historyCollectionName = "indexed-history-period";
            await _historyClient.ReplaceOneAsync(
                historyCollectionName,
                new PaymentOperationHistoryRecord
                {
                    Balance = 10m,
                    Record = new FinancialTransaction
                    {
                        Key = operationId,
                        PaymentAccountId = accountId,
                        Amount = 10m,
                        OperationDay = new DateOnly(2024, 1, 1)
                    }
                });

            var categories = _handbooksDatabase.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);
            var contractors = _handbooksDatabase.GetCollection<ContractorDocument>(LedgerDbCollections.Contractors);
            var accounts = _ledgerDatabase.GetCollection<PaymentAccountDocument>(LedgerDbCollections.PaymentAccounts);
            var history = _historyDatabase.GetCollection<PaymentHistoryDocument>(historyCollectionName);

            (await HasUniqueIndexAsync(categories, "Payload.Key")).Should().BeTrue();
            (await HasUniqueIndexAsync(categories, "Payload.CategoryKey")).Should().BeTrue();
            (await HasUniqueIndexAsync(contractors, "Payload.Key")).Should().BeTrue();
            (await HasUniqueIndexAsync(contractors, "Payload.ContractorKey")).Should().BeTrue();
            (await HasUniqueIndexAsync(accounts, "Payload.Key")).Should().BeTrue();
            (await HasUniqueIndexAsync(history, "Payload.Record.Key")).Should().BeTrue();
        }

        [Test]
        public async Task PaymentProjectionReplay_IntoEmptyMongo_ShouldWriteActiveStateAndAuditRecord()
        {
            var accountId = Guid.NewGuid();
            var operationDay = new DateOnly(2024, 2, 2);
            var period = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(accountId);
            var firstOperationId = Guid.NewGuid();
            var removedOperationId = Guid.NewGuid();
            var service = new PaymentOperationsHistoryService(_historyClient, _categoryClient);

            var result = await service.SyncHistoryAsync(
                period,
                [
                    BuildPaymentEvent(accountId, firstOperationId, 10m, operationDay, PaymentEventTypes.Added, 1),
                    BuildPaymentEvent(accountId, removedOperationId, 7m, operationDay, PaymentEventTypes.Added, 2),
                    BuildPaymentEvent(accountId, removedOperationId, 7m, operationDay, PaymentEventTypes.Removed, 3)
                ],
                new ProjectionCheckpoint
                {
                    StreamId = "payment-account-stream",
                    Revision = "3",
                    Position = "C:3/P:3"
                });

            var stored = await _historyDatabase.GetCollection<PaymentHistoryDocument>(period)
                .Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                .ToListAsync();
            var audits = await _historyDatabase.GetCollection<ProjectionAuditDocument>("_projection_audit")
                .Find(FilterDefinition<ProjectionAuditDocument>.Empty)
                .ToListAsync();

            result.Payload.Should().Be(10m);
            stored.Should().ContainSingle();
            stored.Single().Payload.Record.Key.Should().Be(firstOperationId);
            audits.Should().ContainSingle(a =>
                a.Payload.StreamId == "payment-account-stream" &&
                a.Payload.Revision == "3" &&
                a.Payload.Status == "Succeeded");
        }

        [Test]
        public async Task PaymentProjectionReplay_IntoNonEmptyMongo_ShouldRemoveStaleRows()
        {
            var accountId = Guid.NewGuid();
            var operationDay = new DateOnly(2024, 4, 10);
            var period = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(accountId);
            var staleOperationId = Guid.NewGuid();
            var activeOperationId = Guid.NewGuid();
            var service = new PaymentOperationsHistoryService(_historyClient, _categoryClient);

            await _historyClient.ReplaceOneAsync(
                period,
                new PaymentOperationHistoryRecord
                {
                    Balance = 99m,
                    Record = new FinancialTransaction
                    {
                        Key = staleOperationId,
                        PaymentAccountId = accountId,
                        Amount = 99m,
                        OperationDay = operationDay
                    }
                });

            await service.SyncHistoryAsync(
                period,
                [BuildPaymentEvent(accountId, activeOperationId, 12m, operationDay, PaymentEventTypes.Added, 1)]);

            var stored = await _historyDatabase.GetCollection<PaymentHistoryDocument>(period)
                .Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                .ToListAsync();

            stored.Should().ContainSingle();
            stored.Single().Payload.Record.Key.Should().Be(activeOperationId);
        }

        [Test]
        public async Task PaymentProjectionReplay_AfterCrashMidRebuild_ShouldRecoverOnNextReplay()
        {
            var accountId = Guid.NewGuid();
            var operationDay = new DateOnly(2024, 5, 5);
            var period = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(accountId);
            var firstOperationId = Guid.NewGuid();
            var secondOperationId = Guid.NewGuid();
            var staleOperationId = Guid.NewGuid();
            var collection = _historyDatabase.GetCollection<PaymentHistoryDocument>(period);
            var staleRunId = Guid.NewGuid();
            var service = new PaymentOperationsHistoryService(_historyClient, _categoryClient);
            var partialCrashState = new[]
            {
                new PaymentHistoryDocument
                {
                    ProjectionRunId = staleRunId,
                    Payload = new PaymentOperationHistoryRecord
                    {
                        Balance = 1m,
                        Record = new FinancialTransaction
                        {
                            Key = firstOperationId,
                            PaymentAccountId = accountId,
                            Amount = 1m,
                            OperationDay = operationDay
                        }
                    }
                },
                new PaymentHistoryDocument
                {
                    ProjectionRunId = staleRunId,
                    Payload = new PaymentOperationHistoryRecord
                    {
                        Balance = 100m,
                        Record = new FinancialTransaction
                        {
                            Key = staleOperationId,
                            PaymentAccountId = accountId,
                            Amount = 100m,
                            OperationDay = operationDay
                        }
                    }
                }
            };

            await collection.InsertManyAsync(partialCrashState);

            await service.SyncHistoryAsync(
                period,
                [
                    BuildPaymentEvent(accountId, firstOperationId, 5m, operationDay, PaymentEventTypes.Added, 1),
                    BuildPaymentEvent(accountId, secondOperationId, 7m, operationDay.AddDays(1), PaymentEventTypes.Added, 2)
                ]);

            var stored = await collection.Find(FilterDefinition<PaymentHistoryDocument>.Empty).ToListAsync();

            stored.Select(x => x.Payload.Record.Key).Should().BeEquivalentTo([firstOperationId, secondOperationId]);
            stored.Should().OnlyContain(x => x.ProjectionRunId != staleRunId);
            stored.Max(x => x.Payload.Balance).Should().Be(12m);
        }

        [Test]
        public async Task PaymentProjectionReplay_WithDuplicateOperationKey_ShouldStoreSingleReadRow()
        {
            var accountId = Guid.NewGuid();
            var operationDay = new DateOnly(2024, 6, 6);
            var period = operationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(accountId);
            var operationId = Guid.NewGuid();
            var service = new PaymentOperationsHistoryService(_historyClient, _categoryClient);

            await service.SyncHistoryAsync(
                period,
                [
                    BuildPaymentEvent(accountId, operationId, 5m, operationDay, PaymentEventTypes.Added, 1),
                    BuildPaymentEvent(accountId, operationId, 15m, operationDay, PaymentEventTypes.Updated, 2)
                ]);

            var stored = await _historyDatabase.GetCollection<PaymentHistoryDocument>(period)
                .Find(FilterDefinition<PaymentHistoryDocument>.Empty)
                .ToListAsync();

            stored.Should().ContainSingle();
            stored.Single().Payload.Record.Amount.Should().Be(15m);
            stored.Single().Payload.Balance.Should().Be(15m);
        }

        [Test]
        public async Task UniqueIndexCreation_WithDirtyDuplicateHandbookKeys_ShouldThrowDiagnosticException()
        {
            var collection = _handbooksDatabase.GetCollection<CategoryDocument>(LedgerDbCollections.Categories);
            await collection.InsertManyAsync(
            [
                new CategoryDocument
                {
                    Payload = new Category(CategoryTypes.Income, ["dirty-duplicate"]) { Key = Guid.NewGuid() }
                },
                new CategoryDocument
                {
                    Payload = new Category(CategoryTypes.Income, ["dirty-duplicate"]) { Key = Guid.NewGuid() }
                },
            ]);

            var act = async () => await _categoryClient.GetAsync();

            await act.Should()
                .ThrowAsync<MongoDuplicateKeyDiagnosticException>()
                .WithMessage("*Payload.CategoryKey*dirty-duplicate*");
        }

        private static PaymentAccount CreatePaymentAccount(Guid accountId, string description)
        {
            return new PaymentAccount
            {
                Key = accountId,
                Agent = "agent",
                InitialBalance = 10m,
                Balance = 10m,
                Currency = "BYN",
                Description = description,
                Type = AccountTypes.Deposit
            };
        }

        private static PaymentOperationEvent BuildPaymentEvent(
            Guid accountId,
            Guid operationId,
            decimal amount,
            DateOnly operationDay,
            PaymentEventTypes eventType,
            long sequenceNumber)
        {
            return new PaymentOperationEvent
            {
                EventType = eventType,
                SequenceNumber = sequenceNumber,
                EnvelopId = Guid.Parse($"00000000-0000-0000-0000-{sequenceNumber:000000000000}"),
                Payload = new FinancialTransaction
                {
                    Key = operationId,
                    PaymentAccountId = accountId,
                    Amount = amount,
                    OperationDay = operationDay
                }
            };
        }

        private static async Task<bool> HasUniqueIndexAsync<TDocument>(
            IMongoCollection<TDocument> collection,
            string fieldPath)
        {
            var indexesCursor = await collection.Indexes.ListAsync();
            var indexes = await indexesCursor.ToListAsync();

            return indexes.Any(index =>
                index.GetValue("unique", false).ToBoolean() &&
                index.GetValue("key").AsBsonDocument.ElementCount == 1 &&
                index.GetValue("key").AsBsonDocument.Contains(fieldPath));
        }
    }
}
