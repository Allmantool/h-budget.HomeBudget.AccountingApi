using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Api.IntegrationTests.TestSources;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category("Integration")]
    public class CategoriesControllerTests : IAsyncDisposable
    {
        private const string ApiHost = $"/{Endpoints.Categories}";

        private readonly CategoriesTestWebApp _sut = new();

        [Test]
        public async Task GetCategories_WhenTryToGetAllCategories_ThenIsSuccessStatusCode()
        {
            var getCategoriesRequest = new RestRequest(ApiHost);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<IReadOnlyCollection<Category>>>(getCategoriesRequest);

            response.IsSuccessful.Should().BeTrue();
        }

        [Test]
        public async Task GetCategoryById_WhenTryToGetExistedCategoryById_ThenIsSuccessStatusCode()
        {
            var saveResult = await SaveCategoryAsync(CategoryTypes.Income, "some-test-nodes");

            var existedCategoryId = saveResult.Payload;

            var getCategoriesRequest = new RestRequest($"{ApiHost}/byId/{existedCategoryId}");

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<Category>>(getCategoriesRequest);

            var payload = response.Data;

            payload.IsSucceeded.Should().BeTrue();
        }

        [Test]
        public async Task GetCategoryById_WhenTryToGetNotExistedCategoryById_ThenIsFailStatusCode()
        {
            var getCategoriesRequest = new RestRequest($"{ApiHost}/byId/1c0112d1-3310-46d7-b8c3-b248002b9a8c");

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<Category>>(getCategoriesRequest);

            var payload = response.Data;

            payload.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public async Task CreateNewCategory_WhenCreateANewOneCategory_ReturnsNewGeneratedGuid()
        {
            var result = await SaveCategoryAsync(CategoryTypes.Expense, "Node1,Node2, Node3");

            Guid.TryParse(result.Payload, out _).Should().BeTrue();
        }

        [Test]
        public async Task CreateNewCategory_WhenTryCreateAlreadyExistedCategory_FailsWithExpectedMessage()
        {
            _ = await SaveCategoryAsync(CategoryTypes.Expense, "duplicated,nodes");

            var duplicatedCategoryResult = await SaveCategoryAsync(CategoryTypes.Expense, "duplicated,nodes");

            Assert.Multiple(() =>
            {
                duplicatedCategoryResult.IsSucceeded.Should().BeFalse();
                duplicatedCategoryResult.StatusMessage.Should().BeEquivalentTo("The category with '1-categoryType,duplicated,nodes' key already exists");
            });
        }

        [TestCaseSource(typeof(CategoryTypesTestCases))]
        public async Task CreateNewCategory_WhenCreateCategory_ReturnsExpectedOutcome(CategoryTypes outcomeOperationType)
        {
            var result = await SaveCategoryAsync(outcomeOperationType, "Node1,Node2");

            var getCategoriesRequestById = new RestRequest($"{ApiHost}/byId/{result.Payload}");

            var getByIdResponse = await _sut.RestHttpClient.ExecuteAsync<Result<Category>>(getCategoriesRequestById);

            var getByIdPayload = getByIdResponse.Data;

            getByIdPayload.Payload.CategoryType.Should().Be(outcomeOperationType);
        }

        private async Task<Result<string>> SaveCategoryAsync(CategoryTypes categoryType, string categoryNode)
        {
            var requestSaveBody = new CreateCategoryRequest
            {
                CategoryType = categoryType.Id,
                NameNodes =
                [
                    nameof(categoryType),
                    categoryNode
                ]
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.Categories}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<string>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data;
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
