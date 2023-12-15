using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Contractor;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    // [Ignore("Intend to be used only for local testing. Not appropriate infrastructure has been setup")]
    [TestFixture]
    [Category("Integration")]
    public class ContractorsControllerTests : IAsyncDisposable
    {
        private const string ApiHost = $"/{Endpoints.Contractors}";

        private readonly ContractorsTestWebApp _sut = new();

        [Test]
        public async Task GetContractors_WhenTryToGetAllContractors_ThenIsSuccessStatusCode()
        {
            var getContractorsRequest = new RestRequest(ApiHost);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<IReadOnlyCollection<Contractor>>>(getContractorsRequest);

            response.IsSuccessful.Should().BeTrue();
        }

        [Test]
        public async Task GetContractorById_WhenTryToGetExistedById_ThenIsSuccessStatusCode()
        {
            var getContractorsRequest = new RestRequest($"{ApiHost}/byId/66e81106-9214-41a4-8297-82d6761f1d40");

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<Contractor>>(getContractorsRequest);

            var payload = response.Data;

            payload.IsSucceeded.Should().BeTrue();
        }

        [Test]
        public async Task GetContractorById_WhenTryToGetNotExistedById_ThenIsFailStatusCode()
        {
            var getContractorsRequest = new RestRequest($"{ApiHost}/byId/b4a1bc33-a50f-4c9d-aac4-761dfec063dc");

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<Contractor>>(getContractorsRequest);

            var payload = response.Data;

            payload.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void CreateNewContractor_WhenCreateANewOneContractor_ReturnsNewGeneratedGuid()
        {
            var requestBody = new CreateContractorRequest
            {
                NameNodes = new[] { "Node1", "Node2" }
            };

            var postCreateContractorRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = _sut.RestHttpClient.Execute<Result<string>>(postCreateContractorRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
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
