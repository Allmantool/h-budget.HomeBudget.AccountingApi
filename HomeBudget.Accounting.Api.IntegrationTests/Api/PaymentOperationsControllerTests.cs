using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Operation;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class PaymentOperationsControllerTests : IAsyncDisposable
    {
        private const string ApiHost = "payment-operations";

        private readonly OperationsTestWebApp _sut = new();

        [Test]
        public void GetPaymentOperations_WhenTryToGetAllOperations_ThenIsSuccessStatusCode()
        {
            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";
            var getOperationsRequest = new RestRequest($"{ApiHost}/{accountId}");

            var response = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<PaymentOperation>>>(getOperationsRequest);

            Assert.IsTrue(response.IsSuccessful);
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
        public void CreateNewOperation_WhenCreateAnOperation_ReturnsNewGeneratedGuid()
        {
            var operationAmountBefore = MockStore.PaymentOperations.Count;

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = MockStore.Categories.First().Key.ToString(),
                ContractorId = MockStore.Contractors.First().Key.ToString(),
            };

            const string accountId = "92e8c2b2-97d9-4d6d-a9b7-48cb0d039a84";

            var postCreateRequest = new RestRequest($"{ApiHost}/{accountId}", Method.Post)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(postCreateRequest);

            var result = response.Data;
            var payload = result.Payload;

            var operationAmountAfter = MockStore.PaymentOperations.Count;

            Assert.Multiple(() =>
            {
                Guid.TryParse(payload, out _).Should().BeTrue();
                operationAmountBefore.Should().BeLessThan(operationAmountAfter);
            });
        }

        [Test]
        public void DeleteById_WithValidOperationRef_ThenSuccessful()
        {
            var operationAmountBefore = MockStore.PaymentOperations.Count;

            const string operationId = "20a8ca8e-0127-462c-b854-b2868490f3ec";
            const string accountId = "852530a6-70b0-4040-8912-8558d59d977a";

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<bool>>(deleteOperationRequest);

            var result = response.Data;
            var payload = result.Payload;

            var operationAmountAfter = MockStore.PaymentOperations.Count;

            Assert.Multiple(() =>
            {
                payload.Should().BeTrue();
                operationAmountBefore.Should().BeGreaterThan(operationAmountAfter);
            });
        }

        [Test]
        public void DeleteById_WithInValidOperationRef_ThenFail()
        {
            const string operationId = "invalid-operation-ref";
            const string accountId = "invalid-acc-ref";

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<bool>>(deleteOperationRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.Should().BeFalse();
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
                CategoryId = MockStore.Categories.First().CategoryKey,
                ContractorId = MockStore.Contractors.First().ContractorKey
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(patchUpdateOperation);

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
                CategoryId = MockStore.Categories.First().Key.ToString(),
                ContractorId = MockStore.Contractors.First().Key.ToString()
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{accountId}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(patchUpdateOperation);

            var result = response.Data;

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeTrue();
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
