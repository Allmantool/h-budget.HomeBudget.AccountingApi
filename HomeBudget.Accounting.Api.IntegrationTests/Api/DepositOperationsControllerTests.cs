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
    public class DepositOperationsControllerTests : IAsyncDisposable
    {
        private const string ApiHost = "/operations";

        private readonly OperationsTestWebApp _sut = new();

        [Test]
        public void GetDepositOperations_WhenTryToGetAllOperations_ThenIsSuccessStatusCode()
        {
            var getOperationsRequest = new RestRequest(ApiHost);

            var response = _sut.RestHttpClient.Execute<Result<IReadOnlyCollection<DepositOperation>>>(getOperationsRequest);

            Assert.IsTrue(response.IsSuccessful);
        }

        [Test]
        public void GetOperationById_WhenValidFilterById_ReturnsOperationWithExpectedAmount()
        {
            var operationId = MockStore.DepositOperations.First().Key.ToString();

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/byId/{operationId}");

            var response = _sut.RestHttpClient.Execute<Result<DepositOperation>>(getOperationByIdRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.Amount.Should().Be(35.64m);
        }

        [Test]
        public void GetOperationById_WhenInValidFilterById_ReturnsFalseResult()
        {
            const string operationId = "invalidRef";

            var getOperationByIdRequest = new RestRequest($"{ApiHost}/byId/{operationId}");

            var response = _sut.RestHttpClient.Execute<Result<DepositOperation>>(getOperationByIdRequest);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void CreateNewOperation_WhenCreateAnOperation_ReturnsNewGeneratedGuid()
        {
            var operationAmountBefore = MockStore.DepositOperations.Count;

            var requestBody = new CreateOperationRequest
            {
                Amount = 100,
                Comment = "New operation",
                CategoryId = MockStore.Categories.First().Key.ToString(),
                ContractorId = MockStore.Contractors.First().Key.ToString(),
            };

            var postCreateRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(postCreateRequest);

            var result = response.Data;
            var payload = result.Payload;

            var operationAmountAfter = MockStore.DepositOperations.Count;

            Assert.Multiple(() =>
            {
                Guid.TryParse(payload, out _).Should().BeTrue();
                operationAmountBefore.Should().BeLessThan(operationAmountAfter);
            });
        }

        [Test]
        public void DeleteById_WithValidOperationRef_ThenSuccessful()
        {
            var operationAmountBefore = MockStore.DepositOperations.Count;

            var operationId = MockStore.DepositOperations.Last().Key.ToString();

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{operationId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<bool>>(deleteOperationRequest);

            var result = response.Data;
            var payload = result.Payload;

            var operationAmountAfter = MockStore.DepositOperations.Count;

            Assert.Multiple(() =>
            {
                payload.Should().BeTrue();
                operationAmountBefore.Should().BeGreaterThan(operationAmountAfter);
            });
        }

        [Test]
        public void DeleteById_WithInValidOperationRef_ThenFail()
        {
            const string operationId = "Invalid";

            var deleteOperationRequest = new RestRequest($"{ApiHost}/{operationId}", Method.Delete);

            var response = _sut.RestHttpClient.Execute<Result<bool>>(deleteOperationRequest);

            var result = response.Data;
            var payload = result.Payload;

            payload.Should().BeFalse();
        }

        [Test]
        public void Update_WithInvalid_ThenFail()
        {
            const string operationId = "Invalid";

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some description",
                CategoryId = MockStore.Categories.First().CategoryKey,
                ContractorId = MockStore.Contractors.First().ContractorKey
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{operationId}", Method.Patch)
                .AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(patchUpdateOperation);

            var result = response.Data;

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void Update_WithValid_ThenSuccessful()
        {
            var operationId = MockStore.DepositOperations.First().Key.ToString();

            var requestBody = new UpdateOperationRequest
            {
                Amount = 100,
                Comment = "Some update description",
                CategoryId = MockStore.Categories.First().Key.ToString(),
                ContractorId = MockStore.Contractors.First().Key.ToString()
            };

            var patchUpdateOperation = new RestRequest($"{ApiHost}/{operationId}", Method.Patch)
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
