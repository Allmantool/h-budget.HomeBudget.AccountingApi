using System;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface ICrossAccountsTransferService
    {
        Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token);
    }
}
