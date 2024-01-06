using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts;
using HomeBudget.Components.Categories;
using HomeBudget.Components.Contractors;

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
            var paymentAccountId = Guid.Parse("92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84");

            var operationAmountBefore = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
            };

            var postCreateRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);

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
            var paymentAccountId = Guid.Parse("c9b33506-9a98-4f76-ad8e-17c96858305b");

            var operationsAmountBefore = (await GetHistoryRecordsAsync(paymentAccountId)).Count;

            foreach (var i in Enumerable.Range(1, 7))
            {
                var requestBody = new CreateOperationRequest
                {
                    Amount = 10 + i,
                    Comment = $"New operation - {i}",
                    CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                    ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
                    OperationDate = new DateOnly(2023, 12, 15)
                };

                var postCreateRequest = new RestRequest($"{ApiHost}/{paymentAccountId}", Method.Post)
                    .AddJsonBody(requestBody);

                _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);
            }

            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = _sut.RestHttpClient
                .Execute<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getPaymentHistoryRecordsRequest);

            var operationAmountAfter = paymentsHistoryResponse.Data.Payload.Count;

            operationsAmountBefore.Should().BeLessThan(operationAmountAfter);
        }

        [Test]
        public void CreateNewOperation_WhenCreateAnOperation_BalanceShouldBeIncreased()
        {
            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
            };

            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var balanceBefore = MockAccountsStore.Records.Single(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestBody);

            _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);

            var operationAmountAfter = MockAccountsStore.Records.Single(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            balanceBefore.Should().BeLessThan(operationAmountAfter);
        }

        [Test]
        public async Task DeleteById_WithValidOperationRef_ThenSuccessful()
        {
            var paymentAccountId = Guid.Parse("0dbfb498-83e1-4e02-a2c1-c0761eab8529");

            var requestBody = new CreateOperationRequest
            {
                Amount = 25.24m,
                CategoryId = MockCategoriesStore.Categories.First(c => c.CategoryType == CategoryTypes.Income).Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
                Comment = "Some test",
                OperationDate = new DateOnly(2024, 1, 6),
            };

            var postCreateRequest = new RestRequest($"/{Endpoints.PaymentOperations}/{paymentAccountId}", Method.Post)
                .AddJsonBody(requestBody);

            var postResult = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);

            var newOperationId = postResult.Data.Payload.PaymentOperationId;

            var balanceBefore = MockAccountsStore.Records
                .Single(pa => pa.Key.CompareTo(paymentAccountId) == 0)
                .Balance;

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{paymentAccountId}/{newOperationId}", Method.Delete);

            await _sut.RestHttpClient.ExecuteAsync<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var balanceAfter = MockAccountsStore.Records
                .Single(pa => pa.Key.CompareTo(paymentAccountId) == 0)
                .Balance;

            balanceBefore.Should().BeGreaterThan(balanceAfter);
        }

        [Test]
        public async Task DeleteById_WithValidOperationRef_OperationsAmountShouldBeDescriesed()
        {
            var paymentAccountId = Guid.Parse("852530a6-70b0-4040-8912-8558d59d977a");

            var requestBody = new CreateOperationRequest
            {
                Amount = 25.24m,
                CategoryId = MockCategoriesStore.Categories.First(c => c.CategoryType == CategoryTypes.Income).Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
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
        public void DeleteById_WithInValidOperationRef_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void Update_WithInvalid_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some description",
                CategoryId = MockCategoriesStore.Categories.First().CategoryKey,
                ContractorId = MockContractorsStore.Contractors.First().ContractorKey
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void Update_WithValid_ThenSuccessful()
        {
            const string operationId = "2adb60a8-6367-4b8b-afa0-4ff7f7b1c92c";
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some update description",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString()
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var result = response.Data;

            result.IsSucceeded.Should().BeTrue();
        }

        [Test]
        public async Task Update_WithValid_BalanceShouldBeExpectedlyUpdated()
        {
            var accountId = Guid.Parse("35a40606-3782-4f53-8f64-49649b71ab6f");

            var requestCreateBody = new CreateOperationRequest
            {
                Amount = 12.0m,
                Comment = "New operation",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
            };

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestCreateBody);

            var saveResponseResult = await _sut.RestHttpClient.ExecuteAsync<Result<CreateOperationResponse>>(postCreateRequest);

            var requestUpdateBody = new UpdateOperationRequest
            {
                Amount = 17.22m,
                Comment = "Some update description",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString()
            };

            var balanceBefore = MockAccountsStore.Records.Single(pa => pa.Key.CompareTo(accountId) == 0).Balance;

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{saveResponseResult.Data.Payload.PaymentOperationId}", Method.Patch)
                .AddJsonBody(requestUpdateBody);

            await _sut.RestHttpClient.ExecuteAsync<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var balanceAfter = MockAccountsStore.Records.Single(pa => pa.Key.CompareTo(accountId) == 0).Balance;

            balanceBefore.Should().BeLessThan(balanceAfter);
        }

        private async Task<IReadOnlyCollection<PaymentOperationHistoryRecord>> GetHistoryRecordsAsync(Guid paymentAccountId)
        {
            var getPaymentHistoryRecordsRequest = new RestRequest($"{Endpoints.PaymentsHistory}/{paymentAccountId}");

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>>(getPaymentHistoryRecordsRequest);

            return paymentsHistoryResponse.Data.Payload;
        }

        public ValueTask DisposeAsync()
        {
            return _sut?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
