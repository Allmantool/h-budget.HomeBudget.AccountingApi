using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Api.Models.Contractor;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route("contractors")]
    public class ContractorsController(IContractorFactory contractorFactory) : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<Contractor>> GetContractors()
        {
            return new Result<IReadOnlyCollection<Contractor>>(MockStore.Contractors);
        }

        [HttpGet("byId/{contractorId}")]
        public Result<Contractor> GetContractorById(string contractorId)
        {
            var contractorById = MockStore.Contractors.SingleOrDefault(c => string.Equals(c.Key.ToString(), contractorId, StringComparison.OrdinalIgnoreCase));

            return contractorById == null
                ? new Result<Contractor>(isSucceeded: false, message: $"The contractor with '{contractorId}' hasn't been found")
                : new Result<Contractor>(payload: contractorById);
        }

        [HttpPost]
        public Result<string> CreateNewContractor([FromBody] CreateContractorRequest request)
        {
            var newContractor = contractorFactory.Create(request.NameNodes);

            if (MockStore.Contractors.Select(c => c.ContractorKey).Contains(newContractor.ContractorKey))
            {
                return new Result<string>(isSucceeded: false, message: $"The contractor with '{newContractor.ContractorKey}' key already exists");
            }

            MockStore.Contractors.Add(newContractor);

            return new Result<string>(newContractor.Key.ToString());
        }
    }
}
