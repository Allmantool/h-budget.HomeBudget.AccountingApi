using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface ICrossAccountsTransferService
    {
        Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token);
        Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, LedgerGenerationScope expectedGeneration, CancellationToken token);
        Task<Result<TransferCommandResult>> ApplyIdempotentAsync(
            CrossAccountsTransferPayload payload,
            string idempotencyKey,
            string correlationId,
            CancellationToken token);
        Task<Result<TransferCommandResult>> ApplyIdempotentAsync(
            CrossAccountsTransferPayload payload,
            string idempotencyKey,
            string correlationId,
            LedgerGenerationScope expectedGeneration,
            CancellationToken token);
        Task<Result<IEnumerable<Guid>>> RemoveAsync(RemoveTransferPayload removeTransferPayload, CancellationToken token);
        Task<Result<IEnumerable<Guid>>> RemoveAsync(RemoveTransferPayload removeTransferPayload, LedgerGenerationScope expectedGeneration, CancellationToken token);
        Task<Result<Guid>> UpdateAsync(UpdateTransferPayload updateTransferPayload, CancellationToken token);
        Task<Result<Guid>> UpdateAsync(UpdateTransferPayload updateTransferPayload, LedgerGenerationScope expectedGeneration, CancellationToken token);
        Task<TransferCommandRecord> GetCommandAsync(Guid transferId, string commandId);
        Task<TransferCommandRecord> GetCommandAsync(Guid transferId, string commandId, LedgerGenerationScope expectedGeneration);
        Task<TransferCommandRecord> GetByIdAsync(Guid transferId);
        Task<TransferCommandRecord> GetByIdAsync(Guid transferId, LedgerGenerationScope expectedGeneration);
    }
}
