using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Contractor;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Contractors.Clients.Interfaces;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route(Endpoints.Contractors, Name = Endpoints.Contractors)]
    public class ContractorsController(
        IContractorDocumentsClient contractorDocumentsClient,
        IContractorFactory contractorFactory)
        : ControllerBase
    {
        [HttpGet]
        public async Task<Result<IReadOnlyCollection<Contractor>>> GetContractorsAsync()
        {
            var documentsResult = await contractorDocumentsClient.GetAsync();

            var contractors = documentsResult.Payload
                .Select(d => d.Payload)
                .OrderBy(op => op.NameNodes)
                .ThenBy(op => op.OperationUnixTime)
                .ToList();

            return new Result<IReadOnlyCollection<Contractor>>(contractors);
        }

        [HttpGet("byId/{contractorId}")]
        public async Task<Result<Contractor>> GetContractorByIdAsync(string contractorId)
        {
            if (!Guid.TryParse(contractorId, out var targetContractorId))
            {
                return new Result<Contractor>(
                    isSucceeded: false,
                    message: $"Invalid '{nameof(targetContractorId)}' has been provided");
            }

            var documentResult = await contractorDocumentsClient.GetByIdAsync(targetContractorId);

            if (!documentResult.IsSucceeded)
            {
                return new Result<Contractor>(isSucceeded: false, message: $"The contractor with '{contractorId}' hasn't been found");
            }

            var document = documentResult.Payload;

            return new Result<Contractor>(document.Payload);
        }

        [HttpPost]
        public async Task<Result<string>> CreateNewContractorAsync([FromBody] CreateContractorRequest request)
        {
            var newContractor = contractorFactory.Create(request.NameNodes);

            if (await contractorDocumentsClient.CheckIfExistsAsync(newContractor.ContractorKey))
            {
                return new Result<string>(isSucceeded: false, message: $"The contractor with '{newContractor.ContractorKey}' key already exists");
            }

            await contractorDocumentsClient.InsertOneAsync(newContractor);

            return new Result<string>(newContractor.Key.ToString());
        }
    }
}
