using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Models.Contractor
{
    public record CreateContractorRequest
    {
        public IEnumerable<string> NameNodes { get; set; }
    }
}
