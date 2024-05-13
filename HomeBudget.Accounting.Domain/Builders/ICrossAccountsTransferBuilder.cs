using System;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Builders
{
    public interface ICrossAccountsTransferBuilder
    {
        ICrossAccountsTransferBuilder WithSender(FinancialTransaction senderOperation);
        ICrossAccountsTransferBuilder WithRecipient(FinancialTransaction recipientOperation);

        ICrossAccountsTransferBuilder WithTransferId(Guid transferId);

        Result<CrossAccountsTransferOperation> Build();
    }
}
