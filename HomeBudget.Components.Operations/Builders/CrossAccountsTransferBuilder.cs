using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Builders
{
    internal class CrossAccountsTransferBuilder : ICrossAccountsTransferBuilder
    {
        private PaymentOperation _sender;
        private PaymentOperation _recipient;

        private CrossAccountsTransferOperation _transfer = new();

        public ICrossAccountsTransferBuilder WithSender(PaymentOperation senderOperation)
        {
            _sender = senderOperation;

            return this;
        }

        public ICrossAccountsTransferBuilder WithRecipient(PaymentOperation recipientOperation)
        {
            _recipient = recipientOperation;

            return this;
        }

        private void Reset()
        {
            _transfer = new CrossAccountsTransferOperation();

            _sender = null;
            _recipient = null;
        }

        public Result<CrossAccountsTransferOperation> Build()
        {
            if (_recipient == null || _sender == null)
            {
                return Result<CrossAccountsTransferOperation>.Failure("Recipient and sender operations should be provided");
            }

            _recipient.Key = _transfer.Key;
            _recipient.ContractorId = _sender.PaymentAccountId;

            _sender.Key = _transfer.Key;
            _sender.ContractorId = _recipient.PaymentAccountId;

            _transfer.PaymentOperations =
            [
                _sender,
                _recipient
            ];

            var transferBuildResult = _transfer;

            Reset();

            return Result<CrossAccountsTransferOperation>.Succeeded(transferBuildResult);
        }
    }
}
