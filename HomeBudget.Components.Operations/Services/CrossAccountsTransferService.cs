using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services
{
    internal class CrossAccountsTransferService(
        ISender mediator,
        IPaymentsHistoryDocumentsClient documentsClient,
        IPaymentAccountDocumentClient paymentAccountDocumentClient,
        IFinancialTransactionFactory financialTransactionFactory,
        ICrossAccountsTransferBuilder crossAccountsTransferBuilder,
        ITransferCommandStore transferCommandStore,
        ITransferOutboxRegistrationFactory transferRegistrationFactory)
        : ICrossAccountsTransferService
    {
        public async Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token)
        {
            var transferOperation = await BuildTransferAsync(payload);
            return transferOperation.IsSucceeded
                ? await mediator.Send(new ApplyTransferCommand(transferOperation.Payload), token)
                : Result<Guid>.Failure(transferOperation.StatusMessage);
        }

        public async Task<Result<TransferCommandResult>> ApplyIdempotentAsync(
            CrossAccountsTransferPayload payload,
            string idempotencyKey,
            string correlationId,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200 ||
                string.IsNullOrWhiteSpace(payload.SourceReference) ||
                payload.SenderAmount is null or <= 0m || payload.RecipientAmount is null or <= 0m ||
                string.IsNullOrWhiteSpace(payload.SenderCurrency) || string.IsNullOrWhiteSpace(payload.RecipientCurrency))
            {
                return Result<TransferCommandResult>.Failure(
                    "Idempotent transfer creation requires a valid key, source reference, exact side amounts, and currencies.");
            }

            var keyHash = PaymentCommandFingerprint.HashIdempotencyKey(idempotencyKey);
            var fingerprint = TransferCommandFingerprint.Create(payload);
            var existing = await transferCommandStore.GetByIdempotencyKeyAsync(keyHash);
            if (existing is not null)
            {
                return Result<TransferCommandResult>.Succeeded(ToResult(
                    existing,
                    true,
                    !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal)));
            }

            var transferOperation = await BuildTransferAsync(payload);
            if (!transferOperation.IsSucceeded)
            {
                return Result<TransferCommandResult>.Failure(transferOperation.StatusMessage);
            }

            var context = new TransferCommandContext(
                keyHash,
                fingerprint,
                payload.SourceReference.Trim(),
                payload.SenderAmount.Value,
                payload.RecipientAmount.Value,
                payload.SenderCurrency.Trim(),
                payload.RecipientCurrency.Trim());
            var request = transferRegistrationFactory.Create(transferOperation.Payload, context, correlationId);
            var registration = await transferCommandStore.RegisterAsync(request);
            var conflict = !string.Equals(registration.RequestFingerprint, fingerprint, StringComparison.Ordinal);
            return Result<TransferCommandResult>.Succeeded(new TransferCommandResult(
                registration.TransferId,
                registration.CommandId,
                registration.SenderCommandId,
                registration.RecipientCommandId,
                PaymentCommandStatus.Accepted,
                registration.WasAlreadyAccepted,
                conflict));
        }

        private async Task<Result<CrossAccountsTransferOperation>> BuildTransferAsync(CrossAccountsTransferPayload payload)
        {
            var exactAmounts = payload.SenderAmount.HasValue && payload.RecipientAmount.HasValue;
            var multiplier = payload.CustomConversionMultiplier ?? payload.Multiplier;
            if (!exactAmounts && multiplier <= 0m)
            {
                return Result<CrossAccountsTransferOperation>.Failure("Transfer multiplier must be greater than zero");
            }

            if (exactAmounts)
            {
                var currencies = await ValidateExactCurrenciesAsync(payload);
                if (!currencies.IsSucceeded)
                {
                    return Result<CrossAccountsTransferOperation>.Failure(currencies.StatusMessage);
                }
            }
            else if (payload.CustomConversionMultiplier.HasValue)
            {
                var sameCurrencyResult = await IsSameCurrencyTransferAsync(payload.Sender, payload.Recipient);
                if (!sameCurrencyResult.IsSucceeded)
                {
                    return Result<CrossAccountsTransferOperation>.Failure(sameCurrencyResult.StatusMessage);
                }

                if (sameCurrencyResult.Payload)
                {
                    return Result<CrossAccountsTransferOperation>.Failure("A custom conversion multiplier cannot be used for a same-currency transfer");
                }
            }

            var senderOperation = financialTransactionFactory
                .CreateTransfer(
                    payload.Sender,
                    -Math.Abs(payload.SenderAmount ?? payload.Amount),
                    payload.OperationAt);
            if (!senderOperation.IsSucceeded)
            {
                return Result<CrossAccountsTransferOperation>.Failure(senderOperation.StatusMessage);
            }

            senderOperation.Payload.ConversionMultiplier = exactAmounts ? null : multiplier;

            var recipientOperation = financialTransactionFactory
                .CreateTransfer(
                    payload.Recipient,
                    Math.Abs(payload.RecipientAmount ?? payload.Amount * multiplier),
                    payload.OperationAt);
            if (!recipientOperation.IsSucceeded)
            {
                return Result<CrossAccountsTransferOperation>.Failure(recipientOperation.StatusMessage);
            }

            recipientOperation.Payload.ConversionMultiplier = exactAmounts ? null : multiplier;

            var transferOperation = await crossAccountsTransferBuilder
                .WithSender(senderOperation.Payload)
                .WithRecipient(recipientOperation.Payload)
                .BuildAsync();
            if (!transferOperation.IsSucceeded)
            {
                return Result<CrossAccountsTransferOperation>.Failure(transferOperation.StatusMessage);
            }

            return transferOperation;
        }

        private async Task<Result<bool>> ValidateExactCurrenciesAsync(CrossAccountsTransferPayload payload)
        {
            var sender = await paymentAccountDocumentClient.GetByIdAsync(payload.Sender.ToString());
            var recipient = await paymentAccountDocumentClient.GetByIdAsync(payload.Recipient.ToString());
            if (!sender.IsSucceeded || sender.Payload is null || !recipient.IsSucceeded || recipient.Payload is null)
            {
                return Result<bool>.Failure("Both transfer accounts must exist before transfer acceptance.");
            }

            if (!string.Equals(sender.Payload.Payload.Currency, payload.SenderCurrency, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(recipient.Payload.Payload.Currency, payload.RecipientCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Failure("Transfer-side currency does not match the referenced payment account.");
            }

            return Result<bool>.Succeeded(true);
        }

        private static TransferCommandResult ToResult(TransferCommandRecord record, bool duplicate, bool conflict) => new(
            record.TransferId,
            record.CommandId,
            record.SenderCommandId,
            record.RecipientCommandId,
            TransferCommandLifecycle.Evaluate(record),
            duplicate,
            conflict);

        public Task<TransferCommandRecord> GetCommandAsync(Guid transferId, string commandId) =>
            transferCommandStore.GetAsync(transferId, commandId);

        public Task<TransferCommandRecord> GetByIdAsync(Guid transferId) =>
            transferCommandStore.GetByTransferIdAsync(transferId);

        private async Task<Result<bool>> IsSameCurrencyTransferAsync(Guid senderAccountId, Guid recipientAccountId)
        {
            var senderAccountResult = await paymentAccountDocumentClient.GetByIdAsync(senderAccountId.ToString());
            if (!senderAccountResult.IsSucceeded)
            {
                return Result<bool>.Failure(senderAccountResult.StatusMessage);
            }

            var recipientAccountResult = await paymentAccountDocumentClient.GetByIdAsync(recipientAccountId.ToString());
            if (!recipientAccountResult.IsSucceeded)
            {
                return Result<bool>.Failure(recipientAccountResult.StatusMessage);
            }

            return Result<bool>.Succeeded(senderAccountResult.Payload.Payload.Currency == recipientAccountResult.Payload.Payload.Currency);
        }

        public async Task<Result<IEnumerable<Guid>>> RemoveAsync(RemoveTransferPayload removeTransferPayload, CancellationToken token)
        {
            var transferOperationDocumentForRemove = await documentsClient
                .GetByIdAsync(
                    removeTransferPayload.PaymentAccountId,
                    removeTransferPayload.TransferOperationId);

            if (transferOperationDocumentForRemove is null)
            {
                return Result<IEnumerable<Guid>>.Failure("Sender transfer operation hasn't been found");
            }

            var transferSenderOperationForRemove = transferOperationDocumentForRemove.Payload.Record;

            var linkTransferOperationDocumentForRemove = await documentsClient
                .GetByIdAsync(
                    transferSenderOperationForRemove.ContractorId,
                    removeTransferPayload.TransferOperationId);

            if (linkTransferOperationDocumentForRemove is null)
            {
                return Result<IEnumerable<Guid>>.Failure("Sender transfer operation hasn't been found");
            }

            var transferRecipientOperationForRemove = linkTransferOperationDocumentForRemove.Payload.Record;

            var transferOperation = await crossAccountsTransferBuilder
                .WithSender(transferSenderOperationForRemove)
                .WithRecipient(transferRecipientOperationForRemove)
                .WithTransferId(removeTransferPayload.TransferOperationId)
                .BuildAsync();
            if (!transferOperation.IsSucceeded)
            {
                return Result<IEnumerable<Guid>>.Failure(transferOperation.StatusMessage);
            }

            await mediator.Send(new RemoveTransferCommand(transferOperation.Payload), token);

            return Result<IEnumerable<Guid>>.Succeeded([
                transferSenderOperationForRemove.PaymentAccountId,
                transferRecipientOperationForRemove.PaymentAccountId
            ]);
        }

        public async Task<Result<Guid>> UpdateAsync(UpdateTransferPayload updateTransferPayload, CancellationToken token)
        {
            var senderOperationDocument = await documentsClient
                .GetByIdAsync(
                    updateTransferPayload.Sender,
                    updateTransferPayload.TransferOperationId);
            if (senderOperationDocument is null)
            {
                return Result<Guid>.Failure("Sender transfer operation hasn't been found");
            }

            var senderOperation = senderOperationDocument.Payload.Record;

            var recipientOperationDocument = await documentsClient
                .GetByIdAsync(
                    updateTransferPayload.Recipient,
                    updateTransferPayload.TransferOperationId);
            if (recipientOperationDocument is null)
            {
                return Result<Guid>.Failure("Recipient transfer operation hasn't been found");
            }

            var recipientOperation = recipientOperationDocument.Payload.Record;

            var transferOperation = await crossAccountsTransferBuilder
                .WithSender(senderOperation)
                .WithRecipient(recipientOperation)
                .BuildAsync();
            if (!transferOperation.IsSucceeded)
            {
                return Result<Guid>.Failure(transferOperation.StatusMessage);
            }

            return await mediator.Send(new UpdateTransferCommand(transferOperation.Payload), token);
        }
    }
}
