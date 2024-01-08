using System.Collections.Generic;

namespace HomeBudget.Accounting.Domain.Models
{
    public class Contractor : DomainEntity
    {
        public IEnumerable<string> NameNodes { get; protected set; }

        public string ContractorKey { get; protected set; }

        public Contractor(IEnumerable<string> nameNodes)
        {
            NameNodes = nameNodes;
            ContractorKey = string.Join(',', NameNodes);
        }
    }
}
