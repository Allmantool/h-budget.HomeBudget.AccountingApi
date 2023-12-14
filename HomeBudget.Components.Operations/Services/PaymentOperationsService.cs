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
            var newOperation = operationFactory.Create(
                paymentAccountId,
                payload.Amount,
                payload.Comment,
                payload.CategoryId,
                payload.ContractorId,
                payload.OperationDate);

            return mediator.Send(new SavePaymentOperationCommand(newOperation), token);
        }

        public Task<Result<Guid>> RemoveAsync(Guid paymentAccountId, Guid operationId, CancellationToken token)
        {
            var operationForDelete = MockOperationsHistoryStore.Records
                .Where(op => op.Record.PaymentAccountId.CompareTo(paymentAccountId) == 0)
                .SingleOrDefault(p => p.Record.Key.CompareTo(operationId) == 0);

            return operationForDelete == null
                ? Task.FromResult(new Result<Guid>(isSucceeded: false, message: $"The operation '{operationId}' doesn't exist"))
                : mediator.Send(new RemovePaymentOperationCommand(operationForDelete.Record), token);
        }
    }
}
