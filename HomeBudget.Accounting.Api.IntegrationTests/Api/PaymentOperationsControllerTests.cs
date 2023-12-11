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
using HomeBudget.Components.Operations;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class PaymentOperationsControllerTests : IAsyncDisposable
    {
        private const string ApiHost = $"/{Endpoints.PaymentOperations}";

        private readonly OperationsTestWebApp _sut = new();

        [Test]
        public void GetPaymentOperations_WhenTryToGetAllOperations_ThenIsSuccessStatusCode()
        {
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";
            var getOperationsRequest = new RestRequest($"{ApiHost}/{accountId}");

            var response = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentOperation>>>(getOperationsRequest);

            response.IsSuccessful.Should().BeTrue();
        }

        [Test]
        public void GetOperationById_WhenValidFilterById_ReturnsOperationWithExpectedAmount()
        {
            const string operationId = "2adb60a8-6367-4b8b-afa0-4ff7f7b1c92c";
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/{accountId}/byId/{operationId}");

            var response = _sut.RestHttpClient.Execute<Result<PaymentOperation>>(getOperationByIdRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.Amount.Should().Be(35.64m);
        }

        [Test]
        public void GetOperationById_WhenInValidFilterById_ReturnsFalseResult()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/{accountId}/byId/{operationId}");

            var response = _sut.RestHttpClient.Execute<Result<PaymentOperation>>(getOperationByIdRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void CreateNewOperation_WhenCreateAnOperation_ShouldAddExtraPaymentOperation()
        {
            var operationAmountBefore = MockOperationsStore.PaymentOperations.Count;

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString(),
            };

            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);

            var result = response.Data;
            var payload = result.Payload;

            var operationAmountAfter = MockOperationsStore.PaymentOperations.Count;

            Assert.Multiple(() =>
            {
                Guid.TryParse(payload.PaymentOperationId, out _).Should().BeTrue();
                Guid.TryParse(payload.PaymentAccountId, out _).Should().BeTrue();
                operationAmountBefore.Should().BeLessThan(operationAmountAfter);
            });
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

            var balanceBefore = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestBody);

            _sut.RestHttpClient.Execute<Result<CreateOperationResponse>>(postCreateRequest);

            var operationAmountAfter = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            Assert.Multiple(() =>
            {
                balanceBefore.Should().BeLessThan(operationAmountAfter);
            });
        }

        [Test]
        public void DeleteById_WithValidOperationRef_ThenSuccessful()
        {
            const string operationId = "5a53e3d3-0596-4ade-8aff-f3b3b956d0bd";
            const string accountId = "c9b33506-9a98-4f76-ad8e-17c96858305b";

            var balanceBefore = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            _sut.RestHttpClient.Execute<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var balanceAfter = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            Assert.Multiple(() =>
            {
                balanceBefore.Should().BeGreaterThan(balanceAfter);
            });
        }

        [Test]
        public void DeleteById_WithValidOperationRef_BalanceShouldBeDescriesed()
        {
            var operationAmountBefore = MockOperationsStore.PaymentOperations.Count;

            const string operationId = "20a8ca8e-0127-462c-b854-b2868490f3ec";
            const string accountId = "852530a6-70b0-4040-8912-8558d59d977a";

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<RemoveOperationResponse>>(deleteOperationRequest);

            var result = response.Data;

            var operationAmountAfter = MockOperationsStore.PaymentOperations.Count;

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeTrue();
                operationAmountBefore.Should().BeGreaterThan(operationAmountAfter);
            });
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

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeTrue();
            });
        }

        [Test]
        public void Update_WithValid_BalanceShouldBeExpectedlyUpdated()
        {
            const string operationId = "2adb60a8-6367-4b8b-afa0-4ff7f7b1c92c";
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var requestBody = new UpdateOperationRequest
            {
                Amount = 170,
                Comment = "Some update description",
                CategoryId = MockCategoriesStore.Categories.First().Key.ToString(),
                ContractorId = MockContractorsStore.Contractors.First().Key.ToString()
            };

            var balanceBefore = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            _sut.RestHttpClient.Execute<Result<UpdateOperationResponse>>(patchUpdateOperation);

            var balanceAfter = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(accountId))).Balance;

            Assert.Multiple(() =>
            {
                balanceBefore.Should().BeLessThan(balanceAfter);
            });
        }

        public async ValueTask DisposeAsync()
        {
            if (_sut != null)
            {
                await _sut.DisposeAsync();
            }
        }
    }
}
