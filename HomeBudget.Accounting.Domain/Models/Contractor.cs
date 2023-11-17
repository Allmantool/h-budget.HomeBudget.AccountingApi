using System.Collections.Generic;

namespace HomeBudget.Accounting.Domain.Models
{
    public class Contractor : DomainEntity
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
