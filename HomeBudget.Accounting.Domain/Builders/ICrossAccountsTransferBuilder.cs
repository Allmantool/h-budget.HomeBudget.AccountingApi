using System;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Builders
{
    public interface ICrossAccountsTransferBuilder
    {
        ICrossAccountsTransferBuilder WithSender(FinancialTransaction senderOperation);
        ICrossAccountsTransferBuilder WithRecipient(FinancialTransaction recipientOperation);

        ICrossAccountsTransferBuilder WithTransferId(Guid transferId);

        Task<Result<CrossAccountsTransferOperation>> BuildAsync();
    }
}
