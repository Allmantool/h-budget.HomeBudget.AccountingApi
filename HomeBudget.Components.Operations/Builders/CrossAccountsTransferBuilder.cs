using System;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Builders
{
    internal class CrossAccountsTransferBuilder(IPaymentAccountDocumentClient client) : ICrossAccountsTransferBuilder
    {
        private Guid? _transferId;

        private FinancialTransaction _sender;
        private FinancialTransaction _recipient;

        private CrossAccountsTransferOperation _transfer = new();

        public ICrossAccountsTransferBuilder WithSender(FinancialTransaction senderOperation)
        {
            _sender = senderOperation;

            return this;
        }

        public ICrossAccountsTransferBuilder WithTransferId(Guid transferId)
        {
            _transferId = transferId;

            return this;
        }

        public ICrossAccountsTransferBuilder WithRecipient(FinancialTransaction recipientOperation)
        {
            _recipient = recipientOperation;

            return this;
        }

        private void Reset()
        {
            _transfer = new CrossAccountsTransferOperation();

            _sender = null;
            _recipient = null;

            _transferId = null;
        }

        private async Task<string> GetAccountTransferDescriptionAsync(Guid accountId)
        {
            var accountDocumentResponse = await client.GetByIdAsync(accountId.ToString());

            if (!accountDocumentResponse.IsSucceeded)
            {
                return string.Empty;
            }

            var accountDocument = accountDocumentResponse.Payload;

            var account = accountDocument.Payload;

            return $"{account.Agent} | {account.Description} (${account.Currency})";
        }

        public async Task<Result<CrossAccountsTransferOperation>> BuildAsync()
        {
            if (_recipient == null || _sender == null)
            {
                return Result<CrossAccountsTransferOperation>.Failure("Recipient and sender operations should be provided");
            }

            _recipient.Key = _transferId ?? _transfer.Key;
            _recipient.ContractorId = _sender.PaymentAccountId;

            _recipient.Comment = $"Transfer from {await GetAccountTransferDescriptionAsync(_sender.PaymentAccountId)}";

            _sender.Key = _transferId ?? _transfer.Key;
            _sender.ContractorId = _recipient.PaymentAccountId;

            _sender.Comment = $"Transfer to {await GetAccountTransferDescriptionAsync(_recipient.PaymentAccountId)}";

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
