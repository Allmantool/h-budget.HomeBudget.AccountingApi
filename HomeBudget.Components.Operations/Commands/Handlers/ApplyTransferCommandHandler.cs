using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class ApplyTransferCommandHandler(
        IMapper mapper,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
        : BasePaymentCommandHandler(
            mapper,
            operationsDeliveryHandler,
            fireAndForgetHandler),
            IRequestHandler<ApplyTransferCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(ApplyTransferCommand request, CancellationToken cancellationToken)
        {
            foreach (var paymentOperation in request.PaymentOperations)
            {
                await HandleAsync(new AddPaymentOperationCommand(paymentOperation), cancellationToken);
            }

            return Result<Guid>.Succeeded(request.Key);
        }
    }
}
