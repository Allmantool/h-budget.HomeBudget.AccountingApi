using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.TestSources;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Api
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    [Order(IntegrationTestOrderIndex.CategoriesControllerTests)]
    public class CategoriesControllerTests : BaseIntegrationTests
    {
        private const string ApiHost = $"/{Endpoints.Categories}";

        private readonly CategoriesTestWebApp _sut = new();
        private RestClient _restClient;
        private RestClient _restClientAllowingHttpErrors;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await _sut.InitAsync();
            await base.SetupAsync();

            _restClient = _sut.RestHttpClient;
            _restClientAllowingHttpErrors = _sut.RestHttpClientAllowingHttpErrors;
        }

        [Test]
        public async Task GetCategories_WhenTryToGetAllCategories_ThenIsSuccessStatusCode()
        {
            var getCategoriesRequest = new RestRequest(ApiHost);

            var response = await _restClient.ExecuteAsync<Result<IReadOnlyCollection<CategoryResponse>>>(getCategoriesRequest);

            response.IsSuccessful.Should().BeTrue();
        }

        [Test]
        public async Task GetCategoryById_WhenTryToGetExistedCategoryById_ThenIsSuccessStatusCode()
        {
            var saveResult = await SaveCategoryAsync(CategoryTypes.Income, "some-test-nodes");

            var existedCategoryId = saveResult.Payload;

            var getCategoriesRequest = new RestRequest($"{ApiHost}/byId/{existedCategoryId}");

            var response = await _restClient.ExecuteAsync<Result<CategoryResponse>>(getCategoriesRequest);

            var payload = response.Data;

            payload.IsSucceeded.Should().BeTrue();
        }

        [Test]
        public async Task GetCategoryById_WhenTryToGetNotExistedCategoryById_ThenIsFailStatusCode()
        {
            var getCategoriesRequest = new RestRequest($"{ApiHost}/byId/1c0112d1-3310-46d7-b8c3-b248002b9a8c");

            var response = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<CategoryResponse>>(
                getCategoriesRequest,
                [HttpStatusCode.NotFound]);

            var payload = response.ShouldBeHttpFailureWithDomainFailure(
                HttpStatusCode.NotFound,
                "missing category lookup should return not found");

            payload.StatusMessage.Should().Contain("hasn't been found", response.DescribeResponse());
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
            var requestBody = new CreateCategoryRequest
            {
                CategoryType = CategoryTypes.Expense.Key,
                NameNodes =
                [
                    "categoryType",
                    "duplicated,nodes"
                ]
            };

            var firstCreateRequest = new RestRequest(ApiHost, Method.Post)
                .AddJsonBody(requestBody);
            var firstCreateResponse = await _restClient.ExecuteAsync<Result<Guid>>(firstCreateRequest);

            var duplicatedCategoryRequest = new RestRequest(ApiHost, Method.Post)
                .AddJsonBody(requestBody);
            var duplicatedCategoryResponse = await _restClientAllowingHttpErrors.ExecuteAllowingHttpErrorAsync<Result<Guid>>(
                duplicatedCategoryRequest,
                [HttpStatusCode.Conflict]);

            var duplicatedCategoryResult = duplicatedCategoryResponse.ShouldBeHttpFailureWithDomainFailure(
                HttpStatusCode.Conflict,
                "duplicate category create should return conflict");

            Assert.Multiple(() =>
            {
                firstCreateResponse.ShouldBeHttpSuccessWithDomainSuccess("first category create should succeed");
                duplicatedCategoryResult.StatusMessage.Should().Contain("already exists", duplicatedCategoryResponse.DescribeResponse());
                duplicatedCategoryResult.StatusMessage.Should().Contain("duplicated,nodes", duplicatedCategoryResponse.DescribeResponse());
            });
        }

        [TestCaseSource(typeof(CategoryTypesTestCases))]
        public async Task CreateNewCategory_WhenCreateCategory_ReturnsExpectedOutcome(CategoryTypes outcomeOperationType)
        {
            var result = await SaveCategoryAsync(outcomeOperationType, "Node1,Node2");

            var getCategoriesRequestById = new RestRequest($"{ApiHost}/byId/{result.Payload}");

            var getByIdResponse = await _restClient.ExecuteAsync<Result<CategoryResponse>>(getCategoriesRequestById);

            var getByIdPayload = getByIdResponse.Data;

            getByIdPayload.Payload.CategoryType.Should().Be(outcomeOperationType.Key);
        }

        private async Task<Result<string>> SaveCategoryAsync(CategoryTypes categoryType, string categoryNode)
        {
            var requestSaveBody = new CreateCategoryRequest
            {
                CategoryType = categoryType.Key,
                NameNodes =
                [
                    nameof(categoryType),
                    categoryNode
                ]
            };

            var saveCategoryRequest = new RestRequest($"{Endpoints.Categories}", Method.Post)
                .AddJsonBody(requestSaveBody);

            var paymentsHistoryResponse = await _restClient
                .ExecuteAsync<Result<string>>(saveCategoryRequest);

            return paymentsHistoryResponse.Data;
        }
    }
}
