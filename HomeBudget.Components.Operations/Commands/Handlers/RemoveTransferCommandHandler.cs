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
    internal class RemoveTransferCommandHandler(
        ILogger<RemoveTransferCommandHandler> logger,
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
            IRequestHandler<RemoveTransferCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(RemoveTransferCommand request, CancellationToken cancellationToken)
        {
            var paymentOperations = request.PaymentOperations;

            await Task.WhenAll(paymentOperations.Select(op => HandleAsync(new RemovePaymentOperationCommand(op), cancellationToken)));

            return Result<Guid>.Succeeded(request.Key);
        }
    }
}
