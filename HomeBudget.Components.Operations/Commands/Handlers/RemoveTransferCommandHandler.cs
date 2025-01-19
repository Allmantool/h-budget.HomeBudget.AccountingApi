using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class RemoveTransferCommandHandler(
        IMapper mapper,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
        : BasePaymentCommandHandler(
            mapper,
            fireAndForgetHandler),
            IRequestHandler<RemoveTransferCommand, Result<Guid>>
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
