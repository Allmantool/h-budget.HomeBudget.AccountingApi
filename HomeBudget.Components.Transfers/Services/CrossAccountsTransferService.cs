using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Transfers.Commands.Models;
using HomeBudget.Components.Transfers.Models;
using HomeBudget.Components.Transfers.Services.Interfaces;

namespace HomeBudget.Components.Transfers.Services
{
    internal class CrossAccountsTransferService(ISender mediator, IOperationFactory operationFactory)
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

            return await mediator.Send(new ApplyTransferCommand([senderOperation.Payload, recipientOperation.Payload]), token);
        }
    }
}
