using System;
using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Models.Category
{
    public record CategoryResponse
    {
        public Guid Key { get; set; }
        public int CategoryType { get; set; }
        public IEnumerable<string> NameNodes { get; set; }
        public string CategoryKey { get; set; }
    }
}
