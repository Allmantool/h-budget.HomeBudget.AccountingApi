using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Contractor;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Contractors.Clients.Interfaces;
using HomeBudget.Core.Models;

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

            if (!documentsResult.IsSucceeded)
            {
                return Result<IReadOnlyCollection<Contractor>>.Failure();
            }

            var contractors = documentsResult.Payload
                .Select(d => d.Payload)
                .OrderBy(op => op.ContractorKey)
                .ThenBy(op => op.OperationUnixTime)
                .ToList();

            return Result<IReadOnlyCollection<Contractor>>.Succeeded(contractors);
        }

        [HttpGet("byId/{contractorId}")]
        public async Task<Result<Contractor>> GetContractorByIdAsync(string contractorId)
        {
            if (!Guid.TryParse(contractorId, out var targetContractorId))
            {
                return Result<Contractor>.Failure($"Invalid '{nameof(targetContractorId)}' has been provided");
            }

            var documentResult = await contractorDocumentsClient.GetByIdAsync(targetContractorId);

            if (!documentResult.IsSucceeded || documentResult.Payload == null)
            {
                return Result<Contractor>.Failure($"The contractor with '{contractorId}' hasn't been found");
            }

            var document = documentResult.Payload;

            return Result<Contractor>.Succeeded(document.Payload);
        }

        [HttpPost]
        public async Task<Result<Guid>> CreateNewAsync([FromBody] CreateContractorRequest request)
        {
            var newContractor = contractorFactory.Create(request.NameNodes);

            if (await contractorDocumentsClient.CheckIfExistsAsync(newContractor.ContractorKey))
            {
                return Result<Guid>.Failure($"The contractor with '{newContractor.ContractorKey}' key already exists");
            }

            var saveResult = await contractorDocumentsClient.InsertOneAsync(newContractor);

            return Result<Guid>.Succeeded(saveResult.Payload);
        }
    }
}
