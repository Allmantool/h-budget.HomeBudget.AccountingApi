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
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class ContractorsControllerTests
    {
        private const string ApiHost = $"/{Endpoints.Contractors}";

        private readonly ContractorsTestWebApp _sut = new();

        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            await _sut.ResetAsync();
        }

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
            var saveRequestBody = new CreateContractorRequest
            {
                NameNodes = new[] { "Node-1", "Node-2" }
            };

            var postCreateContractorRequest = new RestRequest(ApiHost, Method.Post)
                .AddJsonBody(saveRequestBody);

            var saveResponse = await _sut.RestHttpClient.ExecuteAsync<Result<string>>(postCreateContractorRequest);

            var getContractorsRequest = new RestRequest($"{ApiHost}/byId/{saveResponse.Data.Payload}");

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
        public async Task CreateNewContractor_WhenCreateANewOneContractor_ReturnsNewGeneratedGuid()
        {
            var requestBody = new CreateContractorRequest
            {
                NameNodes = new[] { "Node1", "Node2" }
            };

            var postCreateContractorRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<string>>(postCreateContractorRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
        }

        [Test]
        public async Task CreateNewContractor_WhenTheSameContractorAlreadyExists_ReturnsExpectedExceptions()
        {
            var requestBody = new CreateContractorRequest
            {
                NameNodes = new[] { "Node1", "Node2" }
            };

            var postCreateContractorRequest = new RestRequest(ApiHost, Method.Post)
                .AddJsonBody(requestBody);

            await _sut.RestHttpClient.ExecuteAsync<Result<string>>(postCreateContractorRequest);
            var secondResponse = await _sut.RestHttpClient.ExecuteAsync<Result<string>>(postCreateContractorRequest);

            secondResponse.Data.IsSucceeded.Should().BeFalse();
        }
    }
}
