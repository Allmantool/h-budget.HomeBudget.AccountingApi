using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class RemoveTransferCommandHandler(
        IMapper mapper,
        ISender sender,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler,
        IPaymentOperationsHistoryService paymentOperationsHistoryService) :
        BasePaymentCommandHandler(
            mapper,
            sender,
            operationsDeliveryHandler,
            fireAndForgetHandler,
            paymentOperationsHistoryService), IRequestHandler<RemoveTransferCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(RemoveTransferCommand request, CancellationToken cancellationToken)
        {
            foreach (var paymentOperation in request.PaymentOperations)
            {
                await HandleAsync(new RemovePaymentOperationCommand(paymentOperation), cancellationToken);
            }

            return Result<Guid>.Succeeded(request.Key);
        }
    }
}
