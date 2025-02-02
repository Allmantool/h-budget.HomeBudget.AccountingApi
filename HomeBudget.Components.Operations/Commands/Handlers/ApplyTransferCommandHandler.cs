using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class ApplyTransferCommandHandler(
        ILogger<ApplyTransferCommandHandler> logger,
        IMapper mapper,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
        : BasePaymentCommandHandler(
            logger,
            mapper,
            fireAndForgetHandler),
            IRequestHandler<ApplyTransferCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(ApplyTransferCommand request, CancellationToken cancellationToken)
        {
            var transferOperationTasks = request.PaymentOperations
                .Select(op => HandleAsync(new AddPaymentOperationCommand(op), cancellationToken));

            await Task.WhenAll(transferOperationTasks);

            return Result<Guid>.Succeeded(request.Key);
        }
    }
}
