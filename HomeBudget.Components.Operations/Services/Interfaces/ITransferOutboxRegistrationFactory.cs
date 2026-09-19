using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    internal interface ITransferOutboxRegistrationFactory
    {
        TransferCommandRegistrationRequest Create(
            CrossAccountsTransferOperation transfer,
            TransferCommandContext context,
            string correlationId);
    }
}
