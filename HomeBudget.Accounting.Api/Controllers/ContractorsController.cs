using System.Collections.Generic;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Api.Models.Contractor;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route("contractors")]
    public class ContractorsController : ControllerBase
    {
        private readonly IContractorFactory _contractorFactory;

        public ContractorsController(IContractorFactory contractorFactory)
        {
            _contractorFactory = contractorFactory;
        }

        [HttpGet]
        public Result<IReadOnlyCollection<Contractor>> GetContractors()
        {
            return new Result<IReadOnlyCollection<Contractor>>(MockStore.Contractors.Values);
        }

        [HttpPost]
        public Result<string> CreateNewContractor([FromBody] CreateContractorRequest request)
        {
            var newContractor = _contractorFactory.Create(request.NameNodes);

            MockStore.Contractors.Add(newContractor.GetHashCode(), newContractor);

            return new Result<string>(newContractor.Id.ToString());
        }
    }
}
