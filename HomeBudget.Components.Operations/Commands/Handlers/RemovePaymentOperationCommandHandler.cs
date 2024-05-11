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
    internal class RemovePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
        : BasePaymentCommandHandler(
                mapper,
                sender,
                operationsDeliveryHandler,
                fireAndForgetHandler,
                paymentOperationsHistoryService),
            IRequestHandler<RemovePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(RemovePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            return await HandleAsync(request, cancellationToken);
        }
    }
}