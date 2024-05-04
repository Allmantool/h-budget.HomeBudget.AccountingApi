using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Transfers.Models;

namespace HomeBudget.Components.Transfers.Services.Interfaces
{
    public interface ICrossAccountsTransferService
    {
        Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token);
    }
}
