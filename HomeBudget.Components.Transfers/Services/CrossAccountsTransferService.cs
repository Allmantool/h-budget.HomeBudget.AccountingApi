using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Transfers.Models;
using HomeBudget.Components.Transfers.Services.Interfaces;

namespace HomeBudget.Components.Transfers.Services
{
    internal class CrossAccountsTransferService(ISender mediator) : ICrossAccountsTransferService
    {
        public async Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }
}
