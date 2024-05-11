using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Factories
{
    public interface IContractorFactory
    {
        Contractor Create(IEnumerable<string> nameNodes);
    }
}
