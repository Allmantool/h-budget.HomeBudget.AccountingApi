using System;

namespace HomeBudget.Components.Operations.Models
{
    public sealed record TransferCommandResult(
        Guid TransferId,
        string CommandId,
        string SenderCommandId,
        string RecipientCommandId,
        PaymentCommandStatus Status,
        bool IsDuplicate,
        bool IsConflict);
}
