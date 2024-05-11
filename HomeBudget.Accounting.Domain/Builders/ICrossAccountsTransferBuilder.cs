using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Builders
{
    public interface ICrossAccountsTransferBuilder
    {
        ICrossAccountsTransferBuilder WithSender(PaymentOperation senderOperation);
        ICrossAccountsTransferBuilder WithRecipient(PaymentOperation recipientOperation);
        Result<CrossAccountsTransferOperation> Build();
    }
}
