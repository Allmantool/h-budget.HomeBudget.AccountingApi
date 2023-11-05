using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Services
{
    public interface ICategoryFactory
    {
        Category Create(CategoryTypes categoryType, IEnumerable<string> nameNodes);
    }
}
