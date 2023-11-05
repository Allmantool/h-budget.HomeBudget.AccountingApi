using System.Collections.Generic;

namespace HomeBudget.Accounting.Domain.Models
{
    public class Category : BaseDomainEntity
    {
        public Category(CategoryTypes categoryType, IEnumerable<string> nameNodes)
        {
            NameNodes = nameNodes;
            CategoryType = categoryType;
        }

        public CategoryTypes CategoryType { get; }

        public IEnumerable<string> NameNodes { get; }

        public override int GetHashCode()
        {
            return string.Join(',', NameNodes).GetHashCode();
        }
    }
}
