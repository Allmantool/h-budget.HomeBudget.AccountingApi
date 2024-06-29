using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class AddPaymentOperationCommandHandler(
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
        IRequestHandler<AddPaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(AddPaymentOperationCommand request, CancellationToken cancellationToken)
        {
            return await HandleAsync(request, cancellationToken);
        }
    }
}
