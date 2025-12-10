using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Models;
using HomeBudget.Core.Handlers;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class RemovePaymentOperationCommandHandler(
        ILogger<ApplyTransferCommandHandler> logger,
        IMapper mapper,
        IExectutionStrategyHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
        : BasePaymentCommandHandler(
                logger,
                mapper,
                fireAndForgetHandler),
            IRequestHandler<RemovePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(RemovePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            return await HandleAsync(request, cancellationToken);
        }
    }
}