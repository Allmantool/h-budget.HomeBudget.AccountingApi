using System;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Builders
{
    public interface ICrossAccountsTransferBuilder
    {
        ICrossAccountsTransferBuilder WithSender(PaymentOperation senderOperation);
        ICrossAccountsTransferBuilder WithRecipient(PaymentOperation recipientOperation);

        ICrossAccountsTransferBuilder WithTransferId(Guid transferId);

        Result<CrossAccountsTransferOperation> Build();
    }
}
