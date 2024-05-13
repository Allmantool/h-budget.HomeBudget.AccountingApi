using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class PaymentOperationsControllerTests : IAsyncDisposable
    {
        private const string ApiHost = $"/{Endpoints.PaymentOperations}";

        private readonly OperationsTestWebApp _sut = new();

        [OneTimeTearDown]
        public async Task StopAsync() => await _sut.StopAsync();

        [Test]
        public async Task CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperationEvent()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var operationAmountBefore = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperationEvent));

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = categoryIdResult.Payload,
                ContractorId = Guid.NewGuid().ToString(),
            };

            var postCreateRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);

            response.IsSuccessful.Should().Be(true);

            var result = response.Data;
            var payload = result.Payload;

            var operationAmountAfter = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            Assert.Multiple(() =>
            {
                Guid.TryParse(payload.PaymentOperationId, out _).Should().BeTrue();
                Guid.TryParse(payload.PaymentAccountId, out _).Should().BeTrue();
                operationAmountBefore.Should().BeLessThan(operationAmountAfter);
            });
        }

        [Test]
        public async Task CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperationHistoryRecord()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var operationsAmountBefore = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            foreach (var i in Enumerable.Range(1, 7))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 10 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = (await SaveCategoryAsync(CategoryTypes.Income, $"{nameof(CreateOperationRequest)}-{i}")).Payload,
                    ContractorId = Guid.NewGuid().ToString(),
                    OperationDate = new DateOnly(2023, 12, 15)
                };

                var postCreateRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);
            }

            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = _sut.RestHttpClient
                .Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getPaymentHistoryRecordsRequest);

            var operationAmountAfter = paymentsHistoryResponse.Data.Payload.Count;

            operationsAmountBefore.Should().BeLessThan(operationAmountAfter);
        }

        [Test]
        public async Task CreateNewOperation_WhenCreateAnOperation_BalanceShouldBeIncreased()
        {
            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(CreateNewOperation_WhenCreateAnOperation_BalanceShouldBeIncreased));

            var accountId = (await SavePaymentAccountAsync()).Payload;

            var balanceBefore = (await GetPaymentsAccountAsync(accountId)).Balance;

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = categoryIdResult.Payload,
                ContractorId = Guid.NewGuid().ToString(),
            };

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestBody);

            await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);

            var operationAmountAfter = (await GetPaymentsAccountAsync(accountId)).Balance;

            operationAmountAfter.Should().Be(balanceBefore + 100);
        }

        [Test]
        public async Task DeleteById_WithValidOperationRef_ThenSuccessful()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(DeleteById_WithValidOperationRef_ThenSuccessful));

            var requestBody = new CreateOperationRequest
            {
                Amount = 25.24m,
                CategoryId = categoryIdResult.Payload,
                ContractorId = Guid.NewGuid().ToString(),
                Comment = "Some test",
                OperationDate = new DateOnly(2024, 1, 6),
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var postResult = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);

            var newOperationId = postResult.Data.Payload.PaymentOperationId;

            var balanceBefore = (await GetPaymentsAccountAsync(paymentAccountId)).Balance;

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{paymentAccountId}/{newOperationId}", Method.Delete);

            await _sut.RestHttpClient.ExecuteAsync<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var balanceAfter = (await GetPaymentsAccountAsync(paymentAccountId)).Balance;

            balanceBefore.Should().BeGreaterThan(balanceAfter);
        }

        [Test]
        public async Task DeleteById_WithValidOperationRef_OperationsAmountShouldBeDescriesed()
        {
            var paymentAccountId = (await SavePaymentAccountAsync()).Payload;

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(DeleteById_WithValidOperationRef_OperationsAmountShouldBeDescriesed));

            var requestBody = new CreateOperationRequest
            {
                Amount = 25.24m,
                CategoryId = categoryIdResult.Payload,
                ContractorId = Guid.NewGuid().ToString(),
                Comment = "Some test",
                OperationDate = new DateOnly(2024, 1, 6),
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var postResult = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);

            var newOperationId = postResult.Data.Payload.PaymentOperationId;

            var operationAmountBefore = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{paymentAccountId}/{newOperationId}", Method.Delete);

            await _sut.RestHttpClient.ExecuteAsync<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var operationAmountAfter = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            operationAmountBefore.Should().BeGreaterThan(operationAmountAfter);
        }

        [Test]
        public async Task DeleteById_WithInValidOperationRef_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public async Task Update_WithInvalid_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(Update_WithInvalid_ThenFail));

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some description",
                CategoryId = categoryIdResult.Payload,
                ContractorId = Guid.NewGuid().ToString()
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public async Task Update_WithValid_ThenSuccessful()
        {
            const string operationId = "2adb60a8-6367-4b8b-afa0-4ff7f7b1c92c";
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(Update_WithValid_ThenSuccessful));

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some update description",
                CategoryId = categoryIdResult.Payload,
                ContractorId = Guid.NewGuid().ToString()
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var result = response.Data;

            result.IsSucceeded.Should().BeTrue();
        }

        [Test]
        public async Task Update_WithValid_BalanceShouldBeExpectedlyUpdated()
        {
            var accountId = (await SavePaymentAccountAsync()).Payload;

            var categoryIdResult = await SaveCategoryAsync(CategoryTypes.Income, nameof(Update_WithValid_BalanceShouldBeExpectedlyUpdated));

            var requestCreateBody = new CreateOperationRequest
            {
                Amount = 12.0m,
                Comment = "New operation",
                CategoryId = categoryIdResult.Payload,
                ContractorId = Guid.NewGuid().ToString(),
            };

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestCreateBody);

            var saveResponseResult = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);

            var requestUpdateBody = new UpdateOperationRequest
            {
                Amount = 17.22m,
                Comment = "Some update description",
                CategoryId = categoryIdResult.Payload,
                ContractorId = Guid.NewGuid().ToString()
            };

            var balanceBefore = (await GetPaymentsAccountAsync(accountId)).Balance;

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{saveResponseResult.Data.Payload.PaymentOperationId}", Method.Patch)
                .AddJsonBody(requestUpdateBody);

            await _sut.RestHttpClient.ExecuteAsync<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var balanceAfter = (await GetPaymentsAccountAsync(accountId)).Balance;

            balanceBefore.Should().BeLessThan(balanceAfter);
        }

        public ValueTask DisposeAsync()
        {
            return _sut?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecord>> GetHistoryRecordsAsync(Guid paymentAccountId)
        {
            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var getResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getPaymentHistoryRecordsRequest);

            return getResponse.Data.Payload;
        }

        private async Task<PaymentAccount> GetPaymentsAccountAsync(Guid paymentAccountId)
        {
            var getPaymentsAccountRequest = new RestRequest($"{Endpoints.PaymentAccounts}/byId/{paymentAccountId}");

            var getResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<PaymentAccount>>(getPaymentsAccountRequest);

            return getResponse.Data.Payload;
        }

        private async Task<Result<string>> SaveCategoryAsync(CategoryTypes categoryType, string category)
        {
            var requestSaveBody = new CreateCategoryRequest
            {
                CategoryType = categoryType.Id,
                NameNodes =
                [
                    nameof(categoryType),
                    category
                ]
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.Categories}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<string>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data;
        }

        private async Task<Result<Guid>> SavePaymentAccountAsync()
        {
            var requestSaveBody = new CreatePaymentAccountRequest
            {
                Balance = 11.2m,
                Description = "test-account",
                AccountType = AccountTypes.Deposit,
                Agent = "Personal",
                Currency = "usd"
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.PaymentAccounts}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<Guid>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data;
        }
    }
}
