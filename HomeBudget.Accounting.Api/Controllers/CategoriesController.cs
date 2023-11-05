using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route("categories")]
    public class CategoriesController : ControllerBase
    {
        private readonly ICategoryFactory _categoryFactory;

        public CategoriesController(ICategoryFactory categoryFactory)
        {
            _categoryFactory = categoryFactory;
        }

        [HttpGet]
        public Result<IReadOnlyCollection<Category>> GetCategories()
        {
            return new Result<IReadOnlyCollection<Category>>(MockStore.Categories.Values);
        }

        [HttpGet("byId/{categoryId}")]
        public Result<Category> GetCategoryById(string categoryId)
        {
            var categoryById = MockStore.Categories.Values.SingleOrDefault(c => string.Equals(c.Id.ToString(), categoryId, StringComparison.OrdinalIgnoreCase));

            return categoryById == null
                ? new Result<Category>(isSucceeded: false, message: $"The category with {categoryId} hasn't been found")
                : new Result<Category>(payload: categoryById);
        }

        [HttpPost]
        public Result<string> CreateNewContractor([FromBody] CreateCategoryRequest request)
        {
            var newCategory = _categoryFactory.Create((CategoryTypes)request.CategoryType, request.NameNodes);

            MockStore.Categories.Add(newCategory.GetHashCode(), newCategory);

            return new Result<string>(newCategory.Id.ToString());
        }
    }
}
