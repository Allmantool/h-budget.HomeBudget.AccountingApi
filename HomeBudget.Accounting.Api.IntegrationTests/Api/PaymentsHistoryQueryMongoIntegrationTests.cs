using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using MongoDB.Driver;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    [Order(IntegrationTestOrderIndex.PaymentsHistoryQueryMongoIntegrationTests)]
    public class PaymentsHistoryQueryMongoIntegrationTests : BaseIntegrationTests
    {
        private const string HistoryDatabaseName = "payments_history_test";

        private readonly OperationsTestWebApp _sut = new();
        private readonly List<string> _createdCollectionNames = [];
        private RestClient _restClient;
        private IMongoDatabase _historyDatabase;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await _sut.InitAsync();
            await base.SetupAsync();

            _restClient = _sut.RestHttpClient;
            _historyDatabase = new MongoClient(TestContainers.MongoDbContainer.GetConnectionString())
                .GetDatabase(HistoryDatabaseName);
        }

        [OneTimeTearDown]
        public new async Task TerminateAsync()
        {
            foreach (var collectionName in _createdCollectionNames)
            {
                await _historyDatabase.DropCollectionAsync(collectionName);
            }

            await base.TerminateAsync();
        }

        [Test]
        public async Task QueryHistory_WithThreeMonthCanonicalFixture_ReturnsBalancesIndependentOfPageAndPresentationSortAsync()
        {
            var accountId = await CreatePaymentAccountAsync(100m);
            var incomeCategoryId = await CreateCategoryAsync(CategoryTypes.Income, "query-canonical-income");
            var expenseCategoryId = await CreateCategoryAsync(CategoryTypes.Expense, "query-canonical-expense");
            var records = new[]
            {
                CreateRecord("00000000-0000-0000-0000-000000000101", accountId, incomeCategoryId, Guid.Empty, new DateOnly(2026, 1, 5), 1000m, 1000m, 1),
                CreateRecord("00000000-0000-0000-0000-000000000102", accountId, expenseCategoryId, Guid.Empty, new DateOnly(2026, 1, 15), 100m, 900m, 2),
                CreateRecord("00000000-0000-0000-0000-000000000103", accountId, expenseCategoryId, Guid.Empty, new DateOnly(2026, 2, 3), 50m, -50m, 3),
                CreateRecord("00000000-0000-0000-0000-000000000104", accountId, incomeCategoryId, Guid.Empty, new DateOnly(2026, 2, 20), 200m, 150m, 4),
                CreateRecord("00000000-0000-0000-0000-000000000105", accountId, expenseCategoryId, Guid.Empty, new DateOnly(2026, 3, 7), 25m, -25m, 5),
                CreateRecord("00000000-0000-0000-0000-000000000106", accountId, expenseCategoryId, Guid.Empty, new DateOnly(2026, 3, 11), 125m, -150m, 6)
            };
            await SeedAsync(accountId, records);

            var expectedBalances = new Dictionary<Guid, decimal>
            {
                [records[0].Record.Key] = 1100m,
                [records[1].Record.Key] = 1000m,
                [records[2].Record.Key] = 950m,
                [records[3].Record.Key] = 1150m,
                [records[4].Record.Key] = 1125m,
                [records[5].Record.Key] = 1000m
            };

            var queryResults = new[]
            {
                await QueryAsync(accountId, pageSize: 2, sortDirection: "desc"),
                await QueryAsync(accountId, pageSize: 3, sortDirection: "asc"),
                await QueryAsync(accountId, pageSize: 2, sortBy: "amount", sortDirection: "asc"),
                await QueryAsync(accountId, pageSize: 5, sortBy: "amount", sortDirection: "desc")
            };

            foreach (var queryResult in queryResults)
            {
                queryResult.TotalCount.Should().Be(records.Length);
                queryResult.Items.Should().OnlyContain(record => expectedBalances.ContainsKey(record.Record.Key));
                queryResult.Items.Should().OnlyContain(record => record.Balance == expectedBalances[record.Record.Key]);
            }

            (await QueryAsync(accountId, page: 2, pageSize: 2, sortDirection: "desc")).Items
                .Select(record => record.Record.Key)
                .Should().Equal(records[3].Record.Key, records[2].Record.Key);

            var dateAscending = await QueryAllPagesAsync(accountId, 2, "date", "asc");
            var amountDescending = await QueryAllPagesAsync(accountId, 5, "amount", "desc");

            dateAscending.Should().HaveCount(records.Length);
            amountDescending.Should().HaveCount(records.Length);
            dateAscending.Should().OnlyContain(record => record.Balance == expectedBalances[record.Record.Key]);
            amountDescending.Should().OnlyContain(record => record.Balance == expectedBalances[record.Record.Key]);
        }

        [Test]
        public async Task QueryHistory_UsesMongoFiltersGlobalPagingAndStableTieBreakingAsync()
        {
            var accountId = await CreatePaymentAccountAsync(0m);
            var incomeCategoryA = await CreateCategoryAsync(CategoryTypes.Income, "query-filter-income-a");
            var incomeCategoryB = await CreateCategoryAsync(CategoryTypes.Income, "query-filter-income-b");
            var expenseCategoryA = await CreateCategoryAsync(CategoryTypes.Expense, "query-filter-expense-a");
            var expenseCategoryB = await CreateCategoryAsync(CategoryTypes.Expense, "query-filter-expense-b");
            var contractorA = Guid.Parse("00000000-0000-0000-0000-00000000a001");
            var contractorB = Guid.Parse("00000000-0000-0000-0000-00000000a002");
            var records = new[]
            {
                CreateRecord("00000000-0000-0000-0000-000000000201", accountId, incomeCategoryA, contractorA, new DateOnly(2026, 1, 5), 10m, 10m, 1),
                CreateRecord("00000000-0000-0000-0000-000000000202", accountId, expenseCategoryA, contractorA, new DateOnly(2026, 1, 10), 20m, -10m, 2),
                CreateRecord("00000000-0000-0000-0000-000000000203", accountId, incomeCategoryB, contractorB, new DateOnly(2026, 1, 10), 20m, 10m, 3),
                CreateRecord("00000000-0000-0000-0000-000000000204", accountId, expenseCategoryB, Guid.Empty, new DateOnly(2026, 1, 20), 30m, -20m, 4),
                CreateRecord("00000000-0000-0000-0000-000000000205", accountId, incomeCategoryA, contractorA, new DateOnly(2026, 2, 1), 40m, 40m, 5),
                CreateRecord("00000000-0000-0000-0000-000000000206", accountId, expenseCategoryA, contractorB, new DateOnly(2026, 2, 10), 50m, -10m, 6),
                CreateRecord("00000000-0000-0000-0000-000000000207", accountId, expenseCategoryA, contractorA, new DateOnly(2026, 2, 10), 50m, -60m, 7),
                CreateRecord("00000000-0000-0000-0000-000000000208", accountId, incomeCategoryB, Guid.Empty, new DateOnly(2026, 2, 20), 60m, 0m, 8),
                CreateRecord("00000000-0000-0000-0000-000000000209", accountId, expenseCategoryB, contractorA, new DateOnly(2026, 3, 1), 70m, -70m, 9),
                CreateRecord("00000000-0000-0000-0000-000000000210", accountId, incomeCategoryA, contractorB, new DateOnly(2026, 3, 10), 80m, 10m, 10),
                CreateRecord("00000000-0000-0000-0000-000000000211", accountId, expenseCategoryA, contractorA, new DateOnly(2026, 3, 10), 80m, -70m, 11),
                CreateRecord("00000000-0000-0000-0000-000000000212", accountId, incomeCategoryB, Guid.Empty, new DateOnly(2026, 3, 20), 90m, 20m, 12)
            };
            await SeedAsync(accountId, records);

            var dateAscending = await QueryAllPagesAsync(accountId, 3, "date", "asc");
            dateAscending.Select(record => record.Record.Key).Should().Equal(records.Select(record => record.Record.Key));
            (await QueryAllPagesAsync(accountId, 3, "date", "desc")).Select(record => record.Record.Key)
                .Should().Equal(records.Reverse().Select(record => record.Record.Key));

            var amountAscendingExpected = records.OrderBy(record => record.Record.Amount).ThenBy(record => record.Record.Key).Select(record => record.Record.Key);
            var amountDescendingExpected = records.OrderByDescending(record => record.Record.Amount).ThenByDescending(record => record.Record.Key).Select(record => record.Record.Key);
            (await QueryAllPagesAsync(accountId, 3, "amount", "asc")).Select(record => record.Record.Key).Should().Equal(amountAscendingExpected);
            (await QueryAllPagesAsync(accountId, 3, "amount", "desc")).Select(record => record.Record.Key).Should().Equal(amountDescendingExpected);

            var firstSameDate = await QueryAsync(accountId, pageSize: 10, dateFrom: new DateOnly(2026, 1, 10), dateTo: new DateOnly(2026, 1, 10), sortDirection: "asc");
            var secondSameDate = await QueryAsync(accountId, pageSize: 10, dateFrom: new DateOnly(2026, 1, 10), dateTo: new DateOnly(2026, 1, 10), sortDirection: "asc");
            firstSameDate.Items.Select(record => record.Record.Key).Should().Equal(records[1].Record.Key, records[2].Record.Key);
            secondSameDate.Items.Select(record => record.Record.Key).Should().Equal(firstSameDate.Items.Select(record => record.Record.Key));

            (await QueryAsync(accountId, pageSize: 10, dateFrom: new DateOnly(2026, 2, 10))).Items.Should().HaveCount(7);
            (await QueryAsync(accountId, pageSize: 10, dateTo: new DateOnly(2026, 2, 10))).Items.Should().HaveCount(7);
            (await QueryAsync(accountId, pageSize: 10, dateFrom: new DateOnly(2026, 2, 10), dateTo: new DateOnly(2026, 3, 10))).Items.Should().HaveCount(6);
            (await QueryAsync(accountId, pageSize: 10, type: "expense")).Items.Should().OnlyContain(record => record.Record.CategoryId == expenseCategoryA || record.Record.CategoryId == expenseCategoryB);
            (await QueryAsync(accountId, pageSize: 10, categoryId: expenseCategoryA)).Items.Select(record => record.Record.Key).Should().Equal(records[10].Record.Key, records[6].Record.Key, records[5].Record.Key, records[1].Record.Key);
            (await QueryAsync(accountId, pageSize: 10, contractorId: contractorA)).Items.Should().OnlyContain(record => record.Record.ContractorId == contractorA);
            (await QueryAsync(accountId, pageSize: 10, contractorId: contractorB)).Items.Should().OnlyContain(record => record.Record.ContractorId == contractorB);
            (await QueryAsync(accountId, pageSize: 10, contractorId: Guid.Empty)).Items.Should().OnlyContain(record => record.Record.ContractorId == Guid.Empty);
            (await QueryAsync(accountId, pageSize: 10, amountMin: 50m)).Items.Should().OnlyContain(record => record.Record.Amount >= 50m);
            (await QueryAsync(accountId, pageSize: 10, amountMax: 50m)).Items.Should().OnlyContain(record => record.Record.Amount <= 50m);
            (await QueryAsync(accountId, pageSize: 10, amountMin: 50m, amountMax: 50m)).Items.Select(record => record.Record.Key).Should().Equal(records[6].Record.Key, records[5].Record.Key);

            var combined = await QueryAsync(accountId, pageSize: 10, sortBy: "amount", sortDirection: "desc", dateFrom: new DateOnly(2026, 2, 10), dateTo: new DateOnly(2026, 3, 10), type: "expense", categoryId: expenseCategoryA, contractorId: contractorA, amountMin: 50m, amountMax: 80m);
            combined.Items.Select(record => record.Record.Key).Should().Equal(records[10].Record.Key, records[6].Record.Key);

            var firstGlobalPage = await QueryAsync(accountId, pageSize: 5, sortDirection: "asc");
            firstGlobalPage.Items.Select(record => record.Record.Key).Should().Equal(records.Take(5).Select(record => record.Record.Key));
            firstGlobalPage.TotalCount.Should().Be(records.Length);
            firstGlobalPage.TotalPages.Should().Be(3);
            firstGlobalPage.HasPreviousPage.Should().BeFalse();
            firstGlobalPage.HasNextPage.Should().BeTrue();

            var middlePage = await QueryAsync(accountId, page: 2, pageSize: 5, sortDirection: "asc");
            middlePage.Items.Select(record => record.Record.Key).Should().Equal(records.Skip(5).Take(5).Select(record => record.Record.Key));
            middlePage.HasPreviousPage.Should().BeTrue();
            middlePage.HasNextPage.Should().BeTrue();

            var finalPage = await QueryAsync(accountId, page: 3, pageSize: 5, sortDirection: "asc");
            finalPage.Items.Select(record => record.Record.Key).Should().Equal(records.Skip(10).Select(record => record.Record.Key));
            finalPage.HasPreviousPage.Should().BeTrue();
            finalPage.HasNextPage.Should().BeFalse();

            var empty = await QueryAsync(accountId, pageSize: 10, dateFrom: new DateOnly(2027, 1, 1));
            empty.Items.Should().BeEmpty();
            empty.TotalCount.Should().Be(0);
            empty.TotalPages.Should().Be(0);
            empty.HasPreviousPage.Should().BeFalse();
            empty.HasNextPage.Should().BeFalse();

            var outOfRange = await QueryAsync(accountId, page: 99, pageSize: 5);
            outOfRange.Items.Should().BeEmpty();
            outOfRange.TotalCount.Should().Be(records.Length);
            outOfRange.TotalPages.Should().Be(3);
            outOfRange.HasPreviousPage.Should().BeTrue();
            outOfRange.HasNextPage.Should().BeFalse();

            foreach (var collectionName in _createdCollectionNames.Where(name => name.StartsWith(accountId.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                var indexNames = (await _historyDatabase.GetCollection<PaymentHistoryDocument>(collectionName).Indexes.ListAsync()).ToList()
                    .Select(index => index["name"].AsString);
                indexNames.Should().Contain("ix_payments_history_timeline_date");
                indexNames.Should().Contain("ix_payments_history_timeline_amount");
            }
        }

        private async Task<Guid> CreatePaymentAccountAsync(decimal initialBalance)
        {
            var response = await _restClient.ExecuteAsync<Result<Guid>>(new RestRequest($"/{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(new CreatePaymentAccountRequest
                {
                    InitialBalance = initialBalance,
                    Description = "payment-history-query-mongo-test",
                    AccountType = AccountTypes.Deposit.Key,
                    Agent = "integration-test",
                    Currency = "usd"
                }));

            response.IsSuccessful.Should().BeTrue(Describe(response));
            response.Data.IsSucceeded.Should().BeTrue(Describe(response));
            return response.Data.Payload;
        }

        private async Task<Guid> CreateCategoryAsync(CategoryTypes categoryType, string name)
        {
            var response = await _restClient.ExecuteAsync<Result<string>>(new RestRequest($"/{Endpoints.Categories}", Method.Post)
                .AddJsonBody(new CreateCategoryRequest { CategoryType = categoryType.Key, NameNodes = ["query", name] }));

            response.IsSuccessful.Should().BeTrue(Describe(response));
            response.Data.IsSucceeded.Should().BeTrue(Describe(response));
            return Guid.Parse(response.Data.Payload);
        }

        private async Task SeedAsync(Guid accountId, IEnumerable<PaymentOperationHistoryRecord> records)
        {
            foreach (var period in records.GroupBy(record => record.Record.OperationDay.ToFinancialPeriod().ToFinancialMonthIdentifier(accountId)))
            {
                var collectionName = period.Key;
                _createdCollectionNames.Add(collectionName);
                await _historyDatabase.GetCollection<PaymentHistoryDocument>(collectionName)
                    .InsertManyAsync(period.Select(record => new PaymentHistoryDocument { Payload = record }));
            }
        }

        private async Task<PaymentHistoryPageResponse> QueryAsync(
            Guid accountId,
            int page = 1,
            int pageSize = 25,
            string sortBy = "date",
            string sortDirection = "desc",
            DateOnly? dateFrom = null,
            DateOnly? dateTo = null,
            string type = null,
            Guid? categoryId = null,
            Guid? contractorId = null,
            decimal? amountMin = null,
            decimal? amountMax = null)
        {
            var request = new RestRequest($"/{Endpoints.PaymentsHistory}/query/{accountId}")
                .AddQueryParameter("page", page)
                .AddQueryParameter("pageSize", pageSize)
                .AddQueryParameter("sortBy", sortBy)
                .AddQueryParameter("sortDirection", sortDirection);
            AddOptionalQueryParameter(request, "dateFrom", dateFrom?.ToString("yyyy-MM-dd"));
            AddOptionalQueryParameter(request, "dateTo", dateTo?.ToString("yyyy-MM-dd"));
            AddOptionalQueryParameter(request, "type", type);
            AddOptionalQueryParameter(request, "categoryId", categoryId?.ToString());
            AddOptionalQueryParameter(request, "contractorId", contractorId?.ToString());
            AddOptionalQueryParameter(request, "amountMin", amountMin?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddOptionalQueryParameter(request, "amountMax", amountMax?.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var response = await _restClient.ExecuteAsync<Result<PaymentHistoryPageResponse>>(request);
            response.IsSuccessful.Should().BeTrue(Describe(response));
            response.Data.IsSucceeded.Should().BeTrue(Describe(response));
            return response.Data.Payload;
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> QueryAllPagesAsync(Guid accountId, int pageSize, string sortBy, string sortDirection)
        {
            var result = new List<PaymentOperationHistoryRecordResponse>();
            for (var page = 1; ; page++)
            {
                var response = await QueryAsync(accountId, page, pageSize, sortBy, sortDirection);
                result.AddRange(response.Items);
                if (!response.HasNextPage)
                {
                    return result;
                }
            }
        }

        private static PaymentOperationHistoryRecord CreateRecord(string id, Guid accountId, Guid categoryId, Guid contractorId, DateOnly date, decimal amount, decimal periodBalance, long revision)
        {
            return new PaymentOperationHistoryRecord
            {
                StreamRevision = revision,
                Balance = periodBalance,
                Record = new FinancialTransaction
                {
                    Key = Guid.Parse(id),
                    PaymentAccountId = accountId,
                    CategoryId = categoryId,
                    ContractorId = contractorId,
                    TransactionType = TransactionTypes.Payment,
                    Amount = amount,
                    OperationDay = date,
                    OperationUnixTime = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds() + revision
                }
            };
        }

        private static void AddOptionalQueryParameter(RestRequest request, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                request.AddQueryParameter(name, value);
            }
        }

        private static string Describe<T>(RestResponse<Result<T>> response)
        {
            return response == null
                ? "Response was null."
                : $"HTTP {(int)response.StatusCode} {response.StatusCode}, transport-success={response.IsSuccessful}, domain-success={response.Data?.IsSucceeded}, status='{response.Data?.StatusMessage}', content='{response.Content}'";
        }
    }
}
