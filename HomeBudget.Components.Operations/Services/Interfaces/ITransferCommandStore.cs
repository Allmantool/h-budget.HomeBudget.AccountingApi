using System;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    internal interface ITransferCommandStore
    {
        Task<TransferCommandRegistration> RegisterAsync(TransferCommandRegistrationRequest request);

        Task<TransferCommandRecord> GetByIdempotencyKeyAsync(string idempotencyKeyHash);

        Task<TransferCommandRecord> GetAsync(Guid transferId, string commandId);

        Task<TransferCommandRecord> GetByTransferIdAsync(Guid transferId);
    }
}
