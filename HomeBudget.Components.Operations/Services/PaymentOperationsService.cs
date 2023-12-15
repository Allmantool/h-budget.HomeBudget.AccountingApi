using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Operations.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsService(ISender mediator, IOperationFactory operationFactory)
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

        public Task<Result<Guid>> RemoveAsync(Guid paymentAccountId, Guid operationId, CancellationToken token)
        {
            var operationForDelete = MockOperationsHistoryStore.RecordsForAccount(paymentAccountId)
                .Where(op => op.Record.PaymentAccountId.CompareTo(paymentAccountId) == 0)
                .SingleOrDefault(p => p.Record.Key.CompareTo(operationId) == 0);

            return operationForDelete == null
                ? Task.FromResult(new Result<Guid>(isSucceeded: false, message: $"The operation '{operationId}' doesn't exist"))
                : mediator.Send(new RemovePaymentOperationCommand(operationForDelete.Record), token);
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
