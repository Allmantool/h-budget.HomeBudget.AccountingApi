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
        [SetUp]
        public override void SetUp()
        {
            SetUpHttpClient();

            base.SetUp();
        }

        [Test]
        public async Task GetContractors_WhenTryToGetAllContractors_ThenIsSuccessStatusCode()
        {
            var getContractorsRequest = new RestRequest("/contractors");

            var response = await RestHttpClient.ExecuteAsync<Result<IReadOnlyCollection<Contractor>>>(getContractorsRequest);

            var result = response.Data;

            Assert.IsTrue(response.IsSuccessful);
        }

        [Test]
        public async Task GetContractorById_WhenTryToGetById_ThenIsSuccessStatusCode()
        {
            var getContractorsRequest = new RestRequest("/contractors/byId/1c0112d1-3310-46d7-b8c3-b248002b9a8c");

            var response = await RestHttpClient.ExecuteAsync<Result<Contractor>>(getContractorsRequest);

            var result = response.Data;

            Assert.IsTrue(response.IsSuccessful);
        }

        [Test]
        public void CreateNewContractor_WhenCreateANewOneContractor_ReturnsNewGeneratedGuid()
        {
            var requestBody = new CreateContractorRequest
            {
                NameNodes = new[] { "Node1", "Node2" }
            };

            var postCreateContractorRequest = new RestRequest("/contractors", Method.Post).AddJsonBody(requestBody);

            var response = RestHttpClient.Execute<Result<string>>(postCreateContractorRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
        }
    }
}
