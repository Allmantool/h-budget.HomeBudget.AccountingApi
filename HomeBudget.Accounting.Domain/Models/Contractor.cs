using System.Collections.Generic;

namespace HomeBudget.Accounting.Domain.Models
{
    public class Contractor : BaseDomainEntity
    {
        public Contractor(IEnumerable<string> nameNodes)
        {
            NameNodes = nameNodes;
        }

        public IEnumerable<string> NameNodes { get; }

        public override int GetHashCode()
        {
            return string.Join(',', NameNodes).GetHashCode();
        }
    }
}
