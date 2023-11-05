using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Components.Contractors.Factories
{
    internal class CategoryFactory : ICategoryFactory
    {
        public Category Create(CategoryTypes categoryType, IEnumerable<string> nameNodes)
        {
            return new Category(categoryType, nameNodes)
            {
                Id = Guid.NewGuid()
            };
        }
    }
}
