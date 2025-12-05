using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.History;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    [Order(IntegrationTestOrderIndex.PaymentsHistoryControllerTests)]
    public class PaymentsHistoryControllerTests : BaseIntegrationTests
    {
        private const string ApiHost = $"/{Endpoints.PaymentsHistory}";

        private readonly OperationsTestWebApp _sut = new();
        private RestClient _restClient;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await _sut.InitAsync();
            await base.SetupAsync();

            _restClient = _sut.RestHttpClient;
        }

        [Test]
        public async Task GetPaymentOperations_WhenTryToGetAllOperations_ThenIsSuccessStatusCode()
        {
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";
            var getOperationsRequest = new RestRequest($"{ApiHost}/{accountId}");

            var response = await _restClient.ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getOperationsRequest);

            Assert.Multiple(() =>
            {
                response.IsSuccessful.Should().BeTrue();

                var result = response.Data;

                result.IsSucceeded.Should().BeTrue();
                result.Payload.Should().NotBeNull();
            });
        }

        [Test]
        public async Task GetPaymentOperations_WithSeveralPaymentOperations_ThenBalanceHistoryHasBeenCalculatedCorrectly()
        {
            const int createRequestAmount = 3;
            const decimal initialBalance = 11.2m;
            const decimal expectedBalance = 47.2M;

            var paymentAccountOperationResult = await SavePaymentAccountAsync(initialBalance);

            var paymentAccountId = paymentAccountOperationResult.Payload;

            var categoryId = await SaveCategoryAsync(CategoryTypes.Income, "add-test-6");

            foreach (var i in Enumerable.Range(1, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 10 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = categoryId,
                    ContractorId = Guid.NewGuid().ToString(),
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i)
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                await _restClient.ExecuteWithDelayAsync(postCreateRequest, 1000);
            }

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            var balanceAfter = (await GetPaymentsAccountAsync(paymentAccountId)).Balance;

            var historyRecordBalance = historyRecords.OrderBy(r => r.Record.OperationDay).Select(r => r.Balance);

            Assert.Multiple(() =>
            {
                Assert.That(() => historyRecords.Count, Is.EqualTo(createRequestAmount));

                balanceAfter.Should().Be(expectedBalance);
                historyRecordBalance.Should().BeEquivalentTo([22.2m, 34.2m, 47.2m]);
            });
        }

        [Test]
        public async Task GetPaymentOperations_WithExpenseOperations_ReturnsNegativeBalance()
        {
            const decimal expectedBalanceAmount = 2M;
            const decimal initialBalance = 10m;

            var paymentAccountId = (await SavePaymentAccountAsync(initialBalance)).Payload;

            var expenseCategoryId = await SaveCategoryAsync(CategoryTypes.Expense, "add-test-5");

            var requestBody = new CreateOperationRequest
            {
                Amount = 8,
                Comment = "New operation - expense",
                CategoryId = expenseCategoryId,
                ContractorId = Guid.NewGuid().ToString(),
                OperationDate = new DateOnly(2023, 12, 15)
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            await _restClient.ExecuteWithDelayAsync(postCreateRequest, executionDelayInMs: 2000);

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            var balanceAfter = (await GetPaymentsAccountAsync(paymentAccountId)).Balance;

            Assert.Multiple(() =>
            {
                historyRecords.Single().Balance.Should().Be(expectedBalanceAmount);
                balanceAfter.Should().Be(expectedBalanceAmount);
            });
        }

        [Test]
        public async Task GetPaymentOperations_WithSeveralOperations_ShouldReturnExpectedBalanceOperationsOrdering()
        {
            const int createRequestAmount = 3;
            var paymentAccountId = (await SavePaymentAccountAsync(11.2m)).Payload;

            foreach (var i in Enumerable.Range(1, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 7 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = await SaveCategoryAsync(CategoryTypes.Income, $"add-test-loop-{i}"),
                    ContractorId = Guid.NewGuid().ToString(),
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i)
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                await _restClient.ExecuteWithDelayAsync(postCreateRequest, 5000);
            }

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            historyRecords.Select(r => r.Balance).Should().BeEquivalentTo([19.2m, 28.2m, 38.2m]);
        }

        [Test]
        public async Task GetPaymentOperations_WhenUpdateExistedOperation_ReturnsUpToDateOperationState()
        {
            const int createRequestAmount = 3;
            var paymentAccountId = (await SavePaymentAccountAsync(11.2m)).Payload;

            var operationsGuids = new Collection<Guid>();

            foreach (var i in Enumerable.Range(0, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 7,
                    Comment = $"{i} - New operation",
                    CategoryId = await SaveCategoryAsync(CategoryTypes.Income, $"add-test-3s-{i}"),
                    ContractorId = Guid.NewGuid().ToString(),
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i * (i % 2 == 0 ? -3 : 3))
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                var saveResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(postCreateRequest);
                var operationGuid = Guid.Parse(saveResponse.Data.Payload.PaymentOperationId);

                operationsGuids.Add(operationGuid);
            }

            var updateRequest = new UpdateOperationRequest
            {
                Amount = 9120,
                OperationDate = new DateOnly(2027, 1, 17),
                CategoryId = await SaveCategoryAsync(CategoryTypes.Expense, "add-test-3s-0"),
                ContractorId = "238d0940-9d30-4dd2-b52c-c623d732daf4",
                Comment = "0 - Update operation"
            };

            var operationForUpdateKey = operationsGuids.First();

            var updateCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}/{operationForUpdateKey}", Method.Patch)
                .AddJsonBody(updateRequest);

            await _restClient.ExecuteWithDelayAsync<Result<UpdateOperationResponse>>(updateCreateRequest, executionDelayAfterInMs: 8000);

            var historyRecords = (await GetHistoryRecordsAsync(paymentAccountId))
                .OrderBy(r => r.Record.OperationDay)
                .Select(r => r)
                .ToList();

            Assert.Multiple(() =>
            {
                historyRecords.Select(r => r.Record.Amount).Should().BeEquivalentTo([7, 7, 9120]);

                historyRecords.Select(r => r.Balance).Should().BeEquivalentTo([18.2m, 25.2m, -9094.8m]);
            });
        }

        [Test]
        public async Task GetPaymentOperations_WhenAddAndThenUpdate_ReturnsUpToDateOperationState()
        {
            var paymentAccountId = (await SavePaymentAccountAsync(11.2m)).Payload;

            var requestBody = new CreateOperationRequest
            {
                Amount = 7,
                Comment = "New operation - x",
                CategoryId = await SaveCategoryAsync(CategoryTypes.Income, "add-test-2"),
                ContractorId = Guid.NewGuid().ToString(),
                OperationDate = new DateOnly(2023, 12, 15)
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var addResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(
                postCreateRequest,
                executionDelayAfterInMs: 5000);

            var newOperationId = Guid.Parse(addResponse.Data.Payload.PaymentOperationId);

            var updateRequest = new UpdateOperationRequest
            {
                Amount = 11,
                OperationDate = new DateOnly(2027, 1, 17),
                CategoryId = await SaveCategoryAsync(CategoryTypes.Expense, "add-test-2"),
                ContractorId = "66e81106-9214-41a4-8297-82d6761f1d40",
                Comment = "updated state"
            };

            var updateCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}/{newOperationId}", Method.Patch)
                .AddJsonBody(updateRequest);

            await _restClient.ExecuteWithDelayAsync<Result<UpdateOperationResponse>>(
                updateCreateRequest,
                executionDelayAfterInMs: 5000);

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            var targetPaymentHistory = historyRecords.Single(r => r.Record.Key.CompareTo(newOperationId) == 0);

            targetPaymentHistory.Balance.Should().Be(0.2m);
        }

        [Test]
        public async Task GetOperationById_WhenValidFilterById_ReturnsOperationWithExpectedAmount()
        {
            var paymentAccountId = (await SavePaymentAccountAsync(11.2m)).Payload;

            var requestBody = new CreateOperationRequest
            {
                CategoryId = await SaveCategoryAsync(CategoryTypes.Income, "add-test-1"),
                ContractorId = Guid.NewGuid().ToString(),
                Comment = "Some test",
                OperationDate = new DateOnly(2024, 1, 6),
                Amount = 35.64m
            };

            var createPaymentRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var createPaymentResponse = await _restClient.ExecuteWithDelayAsync<Result<CreateOperationResponse>>(createPaymentRequest);
            var createPaymentRequestResult = createPaymentResponse.Data;

            var newPaymentId = createPaymentRequestResult.Payload.PaymentOperationId;

            var getPaymentByIdRequest = new RestRequest($"{ApiHost}/{paymentAccountId}/byId/{newPaymentId}");

            var paymentHistoryRequestResponse = await _restClient.ExecuteWithDelayAsync<Result<PaymentOperationHistoryRecordResponse>>(
                getPaymentByIdRequest,
                executionDelayBeforeInMs: 5500);

            var paymentHistoryResult = paymentHistoryRequestResponse.Data;
            var paymentHistory = paymentHistoryResult.Payload;

            Assert.Multiple(() =>
            {
                createPaymentRequestResult.IsSucceeded.Should().BeTrue();
                paymentHistoryResult.IsSucceeded.Should().BeTrue();
                paymentHistory.Record.Amount.Should().Be(35.64m);
            });
        }

        [Test]
        public async Task GetOperationById_WhenInValidFilterById_ReturnsFalseResult()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/{accountId}/byId/{operationId}");

            var response = await _restClient.ExecuteAsync<Result<PaymentOperationHistoryRecord>>(getOperationByIdRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>> GetHistoryRecordsAsync(Guid paymentAccountId)
        {
            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = await _restClient.ExecuteWithDelayAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecordResponse>>>(
                getPaymentHistoryRecordsRequest,
                executionDelayBeforeInMs: 4000);

            return paymentsHistoryResponse.Data.Payload;
        }

        private async Task<string> SaveCategoryAsync(CategoryTypes categoryType, string categoryNode)
        {
            var requestSaveBody = new CreateCategoryRequest
            {
                CategoryType = categoryType.Key,
                NameNodes =
                [
                    nameof(categoryType),
                    categoryNode
                ]
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.Categories}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _restClient
                .ExecuteAsync<Result<string>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data.Payload;
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync(decimal initialBalance)
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                InitialBalance = initialBalance,
                Description = "test-account",
                AccountType = AccountTypes.Deposit.Key,
                Agent = "Personal",
                Currency = "usd"
            };

            var saveAccountRequest = new RestRequest($"{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _restClient.ExecuteWithDelayAsync<Result<Guid>>(
                saveAccountRequest,
                executionDelayAfterInMs: 1000);

            return paymentsHistoryResponse.Data;
        }

        private async Task<PaymentAccount> GetPaymentsAccountAsync(Guid paymentAccountId)
        {
            var getPaymentsAccountRequest = new RestRequest($"{Endpoints.PaymentAccounts}/byId/{paymentAccountId}");

            var getResponse = await _restClient
                .ExecuteWithDelayAsync<Result<PaymentAccount>>(getPaymentsAccountRequest, executionDelayAfterInMs: 3500);

            return getResponse.Data.Payload;
        }
    }
}
