using System.Collections.Generic;

namespace HomeBudget.Accounting.Api.Models.Contractor
{
    public class CreateContractorRequest
    {
        public IEnumerable<string> NameNodes { get; set; }
    }
}
