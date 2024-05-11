using System;
using System.Collections.Generic;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Contractors.Factories
{
    public class ContractorFactory : IContractorFactory
    {
        public Contractor Create(IEnumerable<string> nameNodes)
        {
            return new Contractor(nameNodes)
            {
                Key = Guid.NewGuid()
            };
        }
    }
}