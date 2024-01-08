using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Contractors.Models;

namespace HomeBudget.Components.Contractors.Clients.Interfaces
{
    public interface IContractorDocumentsClient : IDocumentClient<ContractorDocument>
    {
        Task<Result<IReadOnlyCollection<ContractorDocument>>> GetAsync();

        Task<Result<ContractorDocument>> GetByIdAsync(Guid contractorId);

        Task<Result<string>> InsertOneAsync(Contractor payload);

        Task<bool> CheckIfExistsAsync(string contractorKey);
    }
}
