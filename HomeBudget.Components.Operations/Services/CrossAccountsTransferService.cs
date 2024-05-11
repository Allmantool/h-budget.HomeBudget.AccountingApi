using System;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Builders;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class CrossAccountsTransferService(
        ISender mediator,
        IOperationFactory operationFactory,
        ICrossAccountsTransferBuilder crossAccountsTransferBuilder)
        : ICrossAccountsTransferService
    {
        public async Task<Result<Guid>> ApplyAsync(CrossAccountsTransferPayload payload, CancellationToken token)
        {
            var senderOperation = operationFactory
                .CreateTransferOperation(
                    payload.Sender,
                    -Math.Abs(payload.Amount),
                    payload.OperationAt);

            var recipientOperation = operationFactory
                .CreateTransferOperation(
                    payload.Recipient,
                    Math.Abs(payload.Amount * payload.Multiplier),
                    payload.OperationAt);

            var transferOperation = crossAccountsTransferBuilder
                .WithSender(senderOperation.Payload)
                .WithRecipient(recipientOperation.Payload)
                .Build();

            return await mediator.Send(new ApplyTransferCommand(transferOperation.Payload), token);
        }
    }
}
