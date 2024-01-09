using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class PaymentsHistoryControllerTests
    {
        private const string ApiHost = $"/{Endpoints.PaymentsHistory}";

        private readonly OperationsTestWebApp _sut = new();

        [Test]
        public void GetPaymentOperations_WhenTryToGetAllOperations_ThenIsSuccessStatusCode()
        {
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";
            var getOperationsRequest = new RestRequest($"{ApiHost}/{accountId}");

            var response = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getOperationsRequest);

            response.IsSuccessful.Should().BeTrue();
        }

        [Test]
        public async Task GetPaymentOperations_WithSeveralPaymentOperations_ThenBalanceHistoryHasBeenCalculatedCorrectly()
        {
            const int createRequestAmount = 11;
            const decimal expectedBalance = 176M;

            var paymentAccountId = Guid.Parse("aed5a7ff-cd0f-4c61-b5ab-a3d7b8f9ac64");

            foreach (var i in Enumerable.Range(1, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 10 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = await SaveCategoryAsync(CategoryTypes.Income),
                    ContractorId = Guid.NewGuid().ToString(),
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i)
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                await _sut.RestHttpClient.ExecuteAsync(postCreateRequest);
            }

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            Assert.Multiple(() =>
            {
                MockAccountsStore.Records.Single(ac => ac.Key.CompareTo(paymentAccountId) == 0).Balance.Should().Be(expectedBalance);

                Assert.That(() => historyRecords.Count, Is.EqualTo(createRequestAmount));
                Assert.That(() => historyRecords.Last().Balance, Is.EqualTo(expectedBalance).After(10));
            });
        }

        [Test]
        public async Task GetPaymentOperations_WithExpenseOperations_ReturnsNegativeBalance()
        {
            const decimal expectedBalanceAmount = 12.13M;
            var paymentAccountId = Guid.Parse("e6739854-7191-4e0a-a655-7d067aecc220");

            var expenseCategoryId = await SaveCategoryAsync(CategoryTypes.Expense);

            var requestBody = new CreateOperationRequest
            {
                Amount = expectedBalanceAmount,
                Comment = "New operation - expense",
                CategoryId = expenseCategoryId.ToString(),
                ContractorId = Guid.NewGuid().ToString(),
                OperationDate = new DateOnly(2023, 12, 15)
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            await _sut.RestHttpClient.ExecuteAsync(postCreateRequest);

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            Assert.Multiple(() =>
            {
                historyRecords.Single().Balance.Should().Be(-expectedBalanceAmount);
                MockAccountsStore.Records.Single(ac => ac.Key.CompareTo(paymentAccountId) == 0).Balance.Should().Be(-expectedBalanceAmount);
            });
        }

        [Test]
        public async Task GetPaymentOperations_WithSeveralOperations_ShouldReturnExpectedBalanceOperationsOrdering()
        {
            const int createRequestAmount = 5;
            var paymentAccountId = Guid.Parse("f38f6c9d-3f1c-4e50-84f9-47d9b5e6a47d");

            foreach (var i in Enumerable.Range(1, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 7 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = await SaveCategoryAsync(CategoryTypes.Income),
                    ContractorId = Guid.NewGuid().ToString(),
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i * (i % 2 == 0 ? -3 : 3))
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                await _sut.RestHttpClient.ExecuteAsync(postCreateRequest);

                // TODO: concurrency issue (skip for now) -- temp workaround
                await Task.Delay(TimeSpan.FromSeconds(0.1), CancellationToken.None);
            }

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            string.Join(',', historyRecords.Select(r => r.Balance)).Should().Be("11,20,28,38,50");
        }

        [Test]
        public async Task GetPaymentOperations_WhenUpdateExistedOperation_ReturnsUpToDateOperationState()
        {
            const int createRequestAmount = 3;
            var paymentAccountId = Guid.Parse("421f203b-fc78-4c7c-93c8-5d56e9aefc30");

            var operationsGuids = new Collection<Guid>();

            foreach (var i in Enumerable.Range(0, createRequestAmount))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 7,
                    Comment = $"New operation - {i}",
                    CategoryId = await SaveCategoryAsync(CategoryTypes.Income),
                    ContractorId = Guid.NewGuid().ToString(),
                    OperationDate = new DateOnly(2023, 12, 15).AddDays(i * (i % 2 == 0 ? -3 : 3))
                };

                var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                var saveResponse = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);
                var operationGuid = Guid.Parse(saveResponse.Data.Payload.PaymentOperationId);

                operationsGuids.Add(operationGuid);

                await Task.Delay(TimeSpan.FromSeconds(0.1), CancellationToken.None);
            }

            var updateRequest = new UpdateOperationRequest
            {
                Amount = 9120,
                OperationDate = new DateOnly(2027, 1, 17),
                CategoryId = "66ce6a56-f61e-4530-8098-b8c58b61a381",
                ContractorId = "238d0940-9d30-4dd2-b52c-c623d732daf4",
                Comment = "updated state"
            };

            var operationForUpdateKey = operationsGuids.First();

            var updateCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}/{operationForUpdateKey}", Method.Patch)
                .AddJsonBody(updateRequest);

            await _sut.RestHttpClient.ExecuteAsync<Result<UpdateOperationResponse>>(updateCreateRequest);

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            var targetPaymentHistory = historyRecords.First(r => r.Record.Key.CompareTo(operationForUpdateKey) == 0);

            Assert.Multiple(() =>
            {
                operationsGuids.Count.Should().Be(3);
                targetPaymentHistory.Record.Amount.Should().Be(9120);
                targetPaymentHistory.Balance.Should().Be(-9106);
            });
        }

        [Test]
        public async Task GetPaymentOperations_WhenAddAndThenUpdate_ReturnsUpToDateOperationState()
        {
            var paymentAccountId = Guid.Parse("4daf3bef-5ffc-4a24-a032-eb97e8593a24");

            var requestBody = new CreateOperationRequest
            {
                Amount = 7,
                Comment = "New operation - x",
                CategoryId = await SaveCategoryAsync(CategoryTypes.Income),
                ContractorId = Guid.NewGuid().ToString(),
                OperationDate = new DateOnly(2023, 12, 15)
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var addResponse = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);
            var newOperationId = Guid.Parse(addResponse.Data.Payload.PaymentOperationId);

            var updateRequest = new UpdateOperationRequest
            {
                Amount = 11,
                OperationDate = new DateOnly(2027, 1, 17),
                CategoryId = "66ce6a56-f61e-4530-8098-b8c58b61a381",
                ContractorId = "66e81106-9214-41a4-8297-82d6761f1d40",
                Comment = "updated state"
            };

            var updateCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}/{newOperationId}", Method.Patch)
                .AddJsonBody(updateRequest);

            await _sut.RestHttpClient.ExecuteAsync<Result<UpdateOperationResponse>>(updateCreateRequest);

            var historyRecords = await GetHistoryRecordsAsync(paymentAccountId);

            var targetPaymentHistory = historyRecords.First(r => r.Record.Key.CompareTo(newOperationId) == 0);

            targetPaymentHistory.Balance.Should().Be(-11);
        }

        [Test]
        public async Task GetOperationById_WhenValidFilterById_ReturnsOperationWithExpectedAmount()
        {
            var paymentAccountId = Guid.Parse("92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84");

            var requestBody = new CreateOperationRequest
            {
                CategoryId = await SaveCategoryAsync(CategoryTypes.Income),
                ContractorId = Guid.NewGuid().ToString(),
                Comment = "Some test",
                OperationDate = new DateOnly(2024, 1, 6),
                Amount = 35.64m
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var postResult = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);

            var newOperationId = postResult.Data.Payload.PaymentOperationId;

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/{paymentAccountId}/byId/{newOperationId}");

            var getResponse = await _sut.RestHttpClient.ExecuteAsync<Result<PaymentOperationHistoryRecord>>(getOperationByIdRequest);

            var result = getResponse.Data;
            var payload = result.Payload;

            payload.Record.Amount.Should().Be(35.64m);
        }

        [Test]
        public void GetOperationById_WhenInValidFilterById_ReturnsFalseResult()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/{accountId}/byId/{operationId}");

            var response = _sut.RestHttpClient.Execute<Result<PaymentOperationHistoryRecord>>(getOperationByIdRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecord>> GetHistoryRecordsAsync(Guid paymentAccountId)
        {
            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getPaymentHistoryRecordsRequest);

            return paymentsHistoryResponse.Data.Payload;
        }

        private async Task<string> SaveCategoryAsync(CategoryTypes categoryType)
        {
            var requestSaveBody = new CreateCategoryRequest
            {
                CategoryType = (int)categoryType,
                NameNodes = new[]
                {
                    nameof(categoryType),
                    "test-category"
                }
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.Categories}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<string>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data.Payload;
        }
    }
}
