using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Services
{
    public interface IContractorFactory
    {
        Contractor Create(IEnumerable<string> nameNodes);
    }
}
