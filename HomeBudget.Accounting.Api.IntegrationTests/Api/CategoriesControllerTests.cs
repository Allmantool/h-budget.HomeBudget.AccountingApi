using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Models;

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
            var existedCategoryId = await SaveCategoryAsync(CategoryTypes.Income, "some-test-nodes");

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
            var requestBody = new CreateCategoryRequest
            {
                CategoryType = (int)CategoryTypes.Expense,
                NameNodes = new[] { "Node1", "Node2", "Node3" }
            };

            var postCreateCategoryRequest = new RestRequest(ApiHost, Method.Post).AddJsonBody(requestBody);

            var response = await _sut.RestHttpClient.ExecuteAsync<Result<string>>(postCreateCategoryRequest);

            var result = response.Data;
            var payload = result.Payload;

            Guid.TryParse(payload, out _).Should().BeTrue();
        }

        [TestCase(1, CategoryTypes.Expense)]
        [TestCase(0, CategoryTypes.Income)]
        public async Task CreateNewCategory_WhenCreateCategory_ReturnsExpectedOutcome(int requestOperationType, CategoryTypes outcomeOperationType)
        {
            var createdCategoryId = await SaveCategoryAsync(outcomeOperationType, "Node1,Node2");

            var getCategoriesRequestById = new RestRequest($"{ApiHost}/byId/{createdCategoryId}");

            var getByIdResponse = await _sut.RestHttpClient.ExecuteAsync<Result<Category>>(getCategoriesRequestById);

            var getByIdPayload = getByIdResponse.Data;

            getByIdPayload.Payload.CategoryType.Should().Be(outcomeOperationType);
        }

        private async Task<string> SaveCategoryAsync(CategoryTypes categoryType, string categoryNode)
        {
            var requestSaveBody = new CreateCategoryRequest
            {
                CategoryType = (int)categoryType,
                NameNodes = new[]
                {
                    nameof(categoryType),
                    categoryNode
                }
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.Categories}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _sut.RestHttpClient
                .ExecuteAsync<Result<string>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data.Payload;
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
