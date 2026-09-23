using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface ICrossAccountsTransferService
    {
        Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token);
        Task<Result<TransferCommandResult>> ApplyIdempotentAsync(
            CrossAccountsTransferPayload payload,
            string idempotencyKey,
            string correlationId,
            CancellationToken token);
        Task<Result<IEnumerable<Guid>>> RemoveAsync(RemoveTransferPayload removeTransferPayload, CancellationToken token);
        Task<Result<Guid>> UpdateAsync(UpdateTransferPayload updateTransferPayload, CancellationToken token);
        Task<TransferCommandRecord> GetCommandAsync(Guid transferId, string commandId);
        Task<TransferCommandRecord> GetByIdAsync(Guid transferId);
    }
}
