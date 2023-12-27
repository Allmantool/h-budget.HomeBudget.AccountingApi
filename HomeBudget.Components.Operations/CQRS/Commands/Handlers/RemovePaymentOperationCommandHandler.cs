using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.CQRS.Commands.Handlers
{
    internal class RemovePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IKafkaDependentProducer<string, string> producer,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
        : BasePaymentCommandHandler(
                mapper,
                sender,
                producer,
                operationsDeliveryHandler,
                paymentOperationsHistoryService),
            IRequestHandler<RemovePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(RemovePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            return await HandleAsync(request, cancellationToken);
        }
    }
}
