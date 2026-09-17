using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Categories.Clients.Interfaces
{
    public interface ICategoryDocumentsClient : IDocumentClient
    {
        Task<Result<IReadOnlyCollection<CategoryDocument>>> GetAsync();

        Task<Result<CategoryDocument>> GetByIdAsync(Guid contractorId);

        Task<Result<Guid>> InsertOneAsync(Category payload);

        Task<IdempotentDocumentWriteResult> InsertIdempotentAsync(
            Category payload,
            IdempotentDocumentWriteContext context,
            CancellationToken cancellationToken);

        Task<bool> CheckIfExistsAsync(string contractorKey);

        Task<Result<IReadOnlyCollection<CategoryDocument>>> GetByIdsAsync(IEnumerable<Guid> operationIds);
    }
}
