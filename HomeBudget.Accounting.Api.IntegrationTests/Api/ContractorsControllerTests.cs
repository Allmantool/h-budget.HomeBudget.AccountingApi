using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Api.Models.Contractor;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    // [Ignore("Intend to be used only for local testing. Not appropriate infrastructure has been setup")]
    [TestFixture]
    [Category("Integration")]
    public class ContractorsControllerTests : BaseWebApplicationFactory<HomeBudgetAccountingApiApplicationFactory<Program>, Program>
    {
        private const string ApiHost = "/contractors";

        [SetUp]
        public override void SetUp()
        {
            SetUpHttpClient();

            base.SetUp();
        }

        [Test]
        public async Task GetContractors_WhenTryToGetAllContractors_ThenIsSuccessStatusCode()
        {
            var getContractorsRequest = new RestRequest(ApiHost);

            var response = await RestHttpClient.ExecuteAsync<Result<IReadOnlyCollection<Contractor>>>(getContractorsRequest);

            Assert.IsTrue(response.IsSuccessful);
        }

        [Test]
        public async Task GetContractorById_WhenTryToGetExistedById_ThenIsSuccessStatusCode()
        {
            var getContractorsRequest = new RestRequest($"{ApiHost}/byId/66e81106-9214-41a4-8297-82d6761f1d40");

            var response = await RestHttpClient.ExecuteAsync<Result<Contractor>>(getContractorsRequest);

            var payload = response.Data;

            Assert.IsTrue(payload.IsSucceeded);
        }

        [Test]
        public async Task GetContractorById_WhenTryToGetNotExistedById_ThenIsFailStatusCode()
        {
            var getContractorsRequest = new RestRequest($"{ApiHost}/byId/b4a1bc33-a50f-4c9d-aac4-761dfec063dc");

            var response = await RestHttpClient.ExecuteAsync<Result<Contractor>>(getContractorsRequest);

            var payload = response.Data;

            Assert.IsFalse(payload.IsSucceeded);
        }

        [Test]
        public void CreateNewContractor_WhenCreateANewOneContractor_ReturnsNewGeneratedGuid()
        {
            var requestBody = new CreateContractorRequest
            {
                NameNodes = new[] { "Node1", "Node2" }
            };

            var postCreateContractorRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = RestHttpClient.Execute<Result<string>>(postCreateContractorRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
        }
    }
}
