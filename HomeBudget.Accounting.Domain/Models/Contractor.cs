using System.Collections.Generic;

namespace HomeBudget.Accounting.Domain.Models
{
    public class Contractor : BaseDomainEntity
    {
        public IEnumerable<string> NameNodes { get; }

        public string ContractorKey { get; }

        public Contractor(IEnumerable<string> nameNodes)
        {
            NameNodes = nameNodes;
            ContractorKey = string.Join(',', NameNodes);
        }
    }
}
