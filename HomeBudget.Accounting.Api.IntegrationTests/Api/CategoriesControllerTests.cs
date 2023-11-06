using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class CategoriesControllerTests : BaseWebApplicationFactory<HomeBudgetAccountingApiApplicationFactory<Program>, Program>
    {
        private const string ApiHost = "/categories";

        [SetUp]
        public override void SetUp()
        {
            SetUpHttpClient();

            base.SetUp();
        }

        [Test]
        public async Task GetCategories_WhenTryToGetAllCategories_ThenIsSuccessStatusCode()
        {
            var getCategoriesRequest = new RestRequest(ApiHost);

            var response = await RestHttpClient.ExecuteAsync<Result<IReadOnlyCollection<Category>>>(getCategoriesRequest);

            Assert.IsTrue(response.IsSuccessful);
        }

        [Test]
        public async Task GetCategoryById_WhenTryToGetExistedCategoryById_ThenIsSuccessStatusCode()
        {
            var getCategoriesRequest = new RestRequest($"{ApiHost}/byId/d5a7f8c7-8b5b-422b-92fa-49a81563f60a");

            var response = await RestHttpClient.ExecuteAsync<Result<Category>>(getCategoriesRequest);

            var payload = response.Data;

            Assert.IsTrue(payload.IsSucceeded);
        }

        [Test]
        public async Task GetCategoryById_WhenTryToGetNotExistedCategoryById_ThenIsFailStatusCode()
        {
            var getCategoriesRequest = new RestRequest($"{ApiHost}/byId/1c0112d1-3310-46d7-b8c3-b248002b9a8c");

            var response = await RestHttpClient.ExecuteAsync<Result<Category>>(getCategoriesRequest);

            var payload = response.Data;

            Assert.IsFalse(payload.IsSucceeded);
        }

        [Test]
        public void CreateNewCategory_WhenCreateANewOneCategory_ReturnsNewGeneratedGuid()
        {
            var requestBody = new CreateCategoryRequest
            {
                CategoryType = (int)CategoryTypes.Expense,
                NameNodes = new[] { "Node1", "Node2", "Node3" }
            };

            var postCreateCategoryRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = RestHttpClient.Execute<Result<string>>(postCreateCategoryRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
        }

        [TestCase(1, CategoryTypes.Expense)]
        [TestCase(0, CategoryTypes.Income)]
        public async Task CreateNewCategory_WhenCreateCategory_ReturnsExpectedOutcome(int requestOperationType, CategoryTypes outcomeOperationType)
        {
            var requestBody = new CreateCategoryRequest
            {
                CategoryType = requestOperationType,
                NameNodes = new[] { "Node1", "Node2" }
            };

            var postCreateCategoryRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var saveResponse = await RestHttpClient.ExecuteAsync<Result<string>>(postCreateCategoryRequest);

            var result = saveResponse.Data;
            var createdCategoryId = result.Payload;

            var getCategoriesRequestById = new RestRequest($"{ApiHost}/byId/{createdCategoryId}");

            var getByIdResponse = await RestHttpClient.ExecuteAsync<Result<Category>>(getCategoriesRequestById);

            var getByIdPayload = getByIdResponse.Data;

            getByIdPayload.Payload.CategoryType.Should().Be(outcomeOperationType);
        }
    }
}
