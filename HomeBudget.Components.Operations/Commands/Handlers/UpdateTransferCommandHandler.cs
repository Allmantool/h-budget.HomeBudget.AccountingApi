using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Handlers;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class UpdateTransferCommandHandler(
        ILogger<UpdateTransferCommandHandler> logger,
        IMapper mapper,
        IDateTimeProvider dateTimeProvider,
        IExectutionStrategyHandler<IKafkaProducer<string, string>> kafkaHandler,
        IExectutionStrategyHandler<IBaseWriteRepository> cdcHandler)
        : BasePaymentCommandHandler(
            logger,
            mapper,
            dateTimeProvider,
            kafkaHandler,
            cdcHandler),
        IRequestHandler<UpdateTransferCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(UpdateTransferCommand request, CancellationToken cancellationToken)
        {
            var transferTasks = request.PaymentOperations.Select(op => HandleAsync(new UpdatePaymentOperationCommand(op), cancellationToken));

            await Task.WhenAll(transferTasks);

            return Result<Guid>.Succeeded(request.Key);
        }
    }
}
