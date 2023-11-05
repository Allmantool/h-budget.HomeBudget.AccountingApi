using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Models.Category
{
    public class CreateCategoryRequest
    {
        public IEnumerable<string> NameNodes { get; set; }

        public int CategoryType { get; set; }
    }
}
