using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Domain.Models
{
    public class Category : DomainEntity
    {
        public CategoryTypes CategoryType { get; protected set; }

        public IEnumerable<string> NameNodes { get; protected set; }

        public string CategoryKey { get; protected set; }

        public Category(CategoryTypes categoryType, IEnumerable<string> nameNodes)
        {
            NameNodes = nameNodes;
            CategoryType = categoryType;

            CategoryKey = $"{categoryType.Id}-{string.Join(',', NameNodes)}";
        }
    }
}
