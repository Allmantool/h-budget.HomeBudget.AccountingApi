using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Categories.Factories
{
    internal class CategoryFactory : ICategoryFactory
    {
        public Category Create(CategoryTypes categoryType, IEnumerable<string> nameNodes)
        {
            return new Category(categoryType, nameNodes)
            {
                Key = Guid.NewGuid()
            };
        }
    }
}
