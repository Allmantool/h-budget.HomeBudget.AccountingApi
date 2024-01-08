using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Categories;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route(Endpoints.Categories, Name = Endpoints.Categories)]
    public class CategoriesController(ICategoryFactory categoryFactory) : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<Category>> GetCategories()
        {
            return new Result<IReadOnlyCollection<Category>>(MockCategoriesStore.Categories.ToList());
        }

        [HttpGet("byId/{categoryId}")]
        public Result<Category> GetCategoryById(string categoryId)
        {
            var categoryById = MockCategoriesStore.Categories.SingleOrDefault(c => string.Equals(c.Key.ToString(), categoryId, StringComparison.OrdinalIgnoreCase));

            return categoryById == null
                ? new Result<Category>(isSucceeded: false, message: $"The category with '{categoryId}' hasn't been found")
                : new Result<Category>(payload: categoryById);
        }

        [HttpPost]
        public Result<string> CreateNewContractor([FromBody] CreateCategoryRequest request)
        {
            var newCategory = categoryFactory.Create((CategoryTypes)request.CategoryType, request.NameNodes);

            if (MockCategoriesStore.Categories.Select(c => c.CategoryKey).Contains(newCategory.CategoryKey))
            {
                return new Result<string>(isSucceeded: false, message: $"The category with '{newCategory.CategoryKey}' key already exists");
            }

            MockCategoriesStore.Categories.Add(newCategory);

            return new Result<string>(newCategory.Key.ToString());
        }
    }
}
