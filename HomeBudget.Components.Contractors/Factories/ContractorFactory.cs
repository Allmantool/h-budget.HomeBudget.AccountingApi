using System;
using System.Collections.Generic;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Components.Contractors.Factories
{
    public class ContractorFactory : IContractorFactory
    {
        public Contractor Create(IEnumerable<string> nameNodes)
        {
            return new Contractor(nameNodes)
            {
                Id = Guid.NewGuid()
            };
        }
    }
}