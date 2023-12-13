using System;
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
        public async Task<Result<Guid>> CreateAsync(
            string paymentAccountId,
            PaymentOperationPayload payload,
            CancellationToken token)
        {
            var newOperation = operationFactory.Create(
                paymentAccountId,
                payload.Amount,
                payload.Comment,
                payload.CategoryId,
                payload.ContractorId,
                payload.OperationDate);

            return await mediator.Send(new SavePaymentOperationCommand(newOperation), token);
        }
    }
}
