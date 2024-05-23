using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

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

            var recipientOperation = financialTransactionFactory
                .CreateTransfer(
                    payload.Recipient,
                    Math.Abs(payload.Amount * payload.Multiplier),
                    payload.OperationAt);

            var transferOperation = await crossAccountsTransferBuilder
                .WithSender(senderOperation.Payload)
                .WithRecipient(recipientOperation.Payload)
                .BuildAsync();

            return await mediator.Send(new ApplyTransferCommand(transferOperation.Payload), token);
        }

        public async Task<Result<IEnumerable<Guid>>> RemoveAsync(RemoveTransferPayload removeTransferPayload, CancellationToken token)
        {
            var transferOperationDocumentForRemove = await documentsClient
                .GetByIdAsync(
                    removeTransferPayload.PaymentAccountId,
                    removeTransferPayload.TransferOperationId);

            var operationForRemove = transferOperationDocumentForRemove.Payload.Record;

            var linkTransferOperationDocumentForRemove = await documentsClient
                .GetByIdAsync(
                    operationForRemove.ContractorId,
                    removeTransferPayload.TransferOperationId);

            var linkOperationForRemove = linkTransferOperationDocumentForRemove.Payload.Record;

            var transferOperation = await crossAccountsTransferBuilder
                .WithSender(operationForRemove)
                .WithRecipient(linkOperationForRemove)
                .WithTransferId(removeTransferPayload.TransferOperationId)
                .BuildAsync();

            await mediator.Send(new RemoveTransferCommand(transferOperation.Payload), token);

            return Result<IEnumerable<Guid>>.Succeeded([
                operationForRemove.PaymentAccountId,
                linkOperationForRemove.PaymentAccountId
            ]);
        }

        public async Task<Result<Guid>> UpdateAsync(UpdateTransferPayload updateTransferPayload, CancellationToken token)
        {
            var senderOperationDocument = await documentsClient
                .GetByIdAsync(
                    updateTransferPayload.Sender,
                    updateTransferPayload.TransferOperationId);

            var senderOperation = senderOperationDocument.Payload.Record;

            var recipientOperationDocument = await documentsClient
                .GetByIdAsync(
                    updateTransferPayload.Recipient,
                    updateTransferPayload.TransferOperationId);

            var recipientOperation = recipientOperationDocument.Payload.Record;

            var transferOperation = await crossAccountsTransferBuilder
                .WithSender(senderOperation)
                .WithRecipient(recipientOperation)
                .BuildAsync();

            return await mediator.Send(new UpdateTransferCommand(transferOperation.Payload), token);
        }
    }
}
