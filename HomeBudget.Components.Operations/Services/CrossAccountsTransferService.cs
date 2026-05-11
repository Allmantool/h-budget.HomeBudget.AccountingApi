using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Factories;
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
        IFinancialTransactionFactory financialTransactionFactory,
        ICrossAccountsTransferBuilder crossAccountsTransferBuilder)
        : ICrossAccountsTransferService
    {
        public async Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token)
        {
            var senderOperation = financialTransactionFactory
                .CreateTransfer(
                    payload.Sender,
                    -Math.Abs(payload.Amount),
                    payload.OperationAt);
            if (!senderOperation.IsSucceeded)
            {
                return Result<Guid>.Failure(senderOperation.StatusMessage);
            }

            var recipientOperation = financialTransactionFactory
                .CreateTransfer(
                    payload.Recipient,
                    Math.Abs(payload.Amount * payload.Multiplier),
                    payload.OperationAt);
            if (!recipientOperation.IsSucceeded)
            {
                return Result<Guid>.Failure(recipientOperation.StatusMessage);
            }

            var transferOperation = await crossAccountsTransferBuilder
                .WithSender(senderOperation.Payload)
                .WithRecipient(recipientOperation.Payload)
                .BuildAsync();
            if (!transferOperation.IsSucceeded)
            {
                return Result<Guid>.Failure(transferOperation.StatusMessage);
            }

            return await mediator.Send(new ApplyTransferCommand(transferOperation.Payload), token);
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
