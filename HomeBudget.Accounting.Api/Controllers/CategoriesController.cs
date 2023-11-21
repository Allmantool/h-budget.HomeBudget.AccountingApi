using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [DisplayName("categories")]
    [Route("categories")]
    public class CategoriesController(ICategoryFactory categoryFactory) : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<Category>> GetCategories()
        {
            return new Result<IReadOnlyCollection<Category>>(MockStore.Categories);
        }

        [HttpGet("byId/{categoryId}")]
        public Result<Category> GetCategoryById(string categoryId)
        {
            var categoryById = MockStore.Categories.SingleOrDefault(c => string.Equals(c.Key.ToString(), categoryId, StringComparison.OrdinalIgnoreCase));

            return categoryById == null
                ? new Result<Category>(isSucceeded: false, message: $"The category with '{categoryId}' hasn't been found")
                : new Result<Category>(payload: categoryById);
        }

        [HttpPost]
        public Result<string> CreateNewContractor([FromBody] CreateCategoryRequest request)
        {
            var newCategory = categoryFactory.Create((CategoryTypes)request.CategoryType, request.NameNodes);

            if (MockStore.Categories.Select(c => c.CategoryKey).Contains(newCategory.CategoryKey))
            {
                return new Result<string>(isSucceeded: false, message: $"The category with '{newCategory.CategoryKey}' key already exists");
            }

            MockStore.Categories.Add(newCategory);

            return new Result<string>(newCategory.Key.ToString());
        }
    }
}
