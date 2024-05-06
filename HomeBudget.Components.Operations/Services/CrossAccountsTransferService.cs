using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class CrossAccountsTransferService(ISender mediator, IOperationFactory operationFactory)
        : ICrossAccountsTransferService
    {
        public async Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token)
        {
            var transferOperation = new TransferOperation();

            var senderOperation = operationFactory
                .CreateTransferOperation(
                    payload.Sender,
                    transferOperation.Key,
                    -Math.Abs(payload.Amount),
                    payload.OperationAt);

            var recipientOperation = operationFactory
                .CreateTransferOperation(
                    payload.Recipient,
                    transferOperation.Key,
                    Math.Abs(payload.Amount * payload.Multiplier),
                    payload.OperationAt);

            transferOperation.PaymentOperations = new List<PaymentOperation>
            {
                senderOperation.Payload,
                recipientOperation.Payload
            };

            return await mediator.Send(new ApplyTransferCommand(transferOperation), token);
        }
    }
}
