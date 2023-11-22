using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Categories
{
    public static class MockCategoriesStore
    {
        public static readonly List<Category> Categories = new()
        {
            new Category(CategoryTypes.Income, new[] { "Income", "Salary" })
            {
                Key = Guid.Parse("0eb283e2-fa49-403d-b7ea-8d6326b3b742")
            },
            new Category(CategoryTypes.Income, new[] { "Transport", "Public" })
            {
                Key = Guid.Parse("d5a7f8c7-8b5b-422b-92fa-49a81563f60a")
            },
            new Category(CategoryTypes.Expense, new[] { "Public services", "Web" })
            {
                Key = Guid.Parse("66ce6a56-f61e-4530-8098-b8c58b61a381")
            },
            new Category(CategoryTypes.Income, new[] { "Transport", "Taxi" })
            {
                Key = Guid.Parse("e9b040ef-6450-46ab-8416-2b146d4da0f0")
            },
            new Category(CategoryTypes.Income, new[] { "Food" })
            {
                Key = Guid.Parse("b4a1bc33-a50f-4c9d-aac4-761dfec063dc")
            },
        };
    }
}
