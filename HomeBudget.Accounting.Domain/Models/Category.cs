using System.Collections.Generic;

namespace HomeBudget.Accounting.Domain.Models
{
    public class Category : BaseDomainEntity
    {
        public CategoryTypes CategoryType { get; }

        public IEnumerable<string> NameNodes { get; }

        public string CategoryKey { get; }

        public Category(CategoryTypes categoryType, IEnumerable<string> nameNodes)
        {
            NameNodes = nameNodes;
            CategoryType = categoryType;

            CategoryKey = $"{(int)CategoryType}-{string.Join(',', NameNodes)}";
        }
    }
}
