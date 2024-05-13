using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Categories.Clients.Interfaces;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route(Endpoints.Categories, Name = Endpoints.Categories)]
    public class CategoriesController(
        ICategoryDocumentsClient categoryDocumentsClient,
        ICategoryFactory categoryFactory) : ControllerBase
    {
        [HttpGet]
        public async Task<Result<IReadOnlyCollection<Category>>> GetCategoriesAsync()
        {
            var documentsResult = await categoryDocumentsClient.GetAsync();

            if (!documentsResult.IsSucceeded)
            {
                return Result<IReadOnlyCollection<Category>>.Failure();
            }

            var categories = documentsResult.Payload
                .Select(d => d.Payload)
                .OrderBy(c => c.CategoryKey)
                .ThenBy(op => op.OperationUnixTime)
                .ToList();

            return Result<IReadOnlyCollection<Category>>.Succeeded(categories);
        }

        [HttpGet("byId/{categoryId}")]
        public async Task<Result<Category>> GetCategoryByIdAsync(string categoryId)
        {
            if (!Guid.TryParse(categoryId, out var targetCategoryId))
            {
                return Result<Category>.Failure($"Invalid '{nameof(targetCategoryId)}' has been provided");
            }

            var documentResult = await categoryDocumentsClient.GetByIdAsync(targetCategoryId);

            if (!documentResult.IsSucceeded || documentResult.Payload == null)
            {
                return Result<Category>.Failure($"The contractor with '{targetCategoryId}' hasn't been found");
            }

            var document = documentResult.Payload;

            return Result<Category>.Succeeded(document.Payload);
        }

        [HttpPost]
        public async Task<Result<Guid>> CreateNewAsync([FromBody] CreateCategoryRequest request)
        {
            var newCategory = categoryFactory.Create(
                BaseEnumeration.FromValue<CategoryTypes>(request.CategoryType),
                request.NameNodes);

            if (await categoryDocumentsClient.CheckIfExistsAsync(newCategory.CategoryKey))
            {
                return Result<Guid>.Failure($"The category with '{newCategory.CategoryKey}' key already exists");
            }

            var saveResult = await categoryDocumentsClient.InsertOneAsync(newCategory);

            return Result<Guid>.Succeeded(saveResult.Payload);
        }
    }
}
