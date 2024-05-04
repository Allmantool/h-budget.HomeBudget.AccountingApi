using MediatR;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Transfers.Commands.Models
{
    internal class ApplyTransferCommand(IEnumerable<TransferOperation> crossAccountsTransfer)
        : IRequest<Result<Guid>>
    {
        public IEnumerable<TransferOperation> CrossAccountsTransfer { get; } = crossAccountsTransfer;
    }
}
