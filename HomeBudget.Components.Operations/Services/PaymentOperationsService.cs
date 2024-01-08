using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsService(
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        ISender mediator,
        IOperationFactory operationFactory)
        : IPaymentOperationsService
    {
        public Task<Result<Guid>> CreateAsync(Guid paymentAccountId, PaymentOperationPayload payload, CancellationToken token)
        {
            var operationForAdd = operationFactory.Create(
                paymentAccountId,
                payload.Amount,
                payload.Comment,
                payload.CategoryId,
                payload.ContractorId,
                payload.OperationDate);

            return mediator.Send(new SavePaymentOperationCommand(operationForAdd), token);
        }

        public async Task<Result<Guid>> RemoveAsync(Guid paymentAccountId, Guid operationId, CancellationToken token)
        {
            var documents = await paymentsHistoryDocumentsClient.GetAsync(paymentAccountId);

            var operationForDelete = documents
                .Where(op => op.Payload.Record.PaymentAccountId.CompareTo(paymentAccountId) == 0)
                .SingleOrDefault(p => p.Payload.Record.Key.CompareTo(operationId) == 0);

            return operationForDelete == null
                ? new Result<Guid>(isSucceeded: false, message: $"The operation '{operationId}' doesn't exist")
                : await mediator.Send(new RemovePaymentOperationCommand(operationForDelete.Payload.Record), token);
        }

        public Task<Result<Guid>> UpdateAsync(Guid paymentAccountId, Guid operationId, PaymentOperationPayload payload, CancellationToken token)
        {
            var operationForUpdate = new PaymentOperation
            {
                PaymentAccountId = paymentAccountId,
                Key = operationId,
                Amount = payload.Amount,
                Comment = payload.Comment,
                CategoryId = Guid.Parse(payload.CategoryId),
                ContractorId = Guid.Parse(payload.ContractorId),
                OperationDay = payload.OperationDate
            };

            return mediator.Send(new UpdatePaymentOperationCommand(operationForUpdate), token);
        }
    }
}
