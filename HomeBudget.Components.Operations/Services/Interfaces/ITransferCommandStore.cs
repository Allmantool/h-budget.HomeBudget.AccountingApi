using System;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    internal interface ITransferCommandStore
    {
        Task<TransferCommandRegistration> RegisterAsync(TransferCommandRegistrationRequest request);

        Task<TransferCommandRecord> GetByIdempotencyKeyAsync(
            string idempotencyKeyHash,
            LedgerGenerationScope generation);

        Task<TransferCommandRecord> GetAsync(
            Guid transferId,
            string commandId,
            LedgerGenerationScope generation);

        Task<TransferCommandRecord> GetByTransferIdAsync(
            Guid transferId,
            LedgerGenerationScope generation);
    }
}
