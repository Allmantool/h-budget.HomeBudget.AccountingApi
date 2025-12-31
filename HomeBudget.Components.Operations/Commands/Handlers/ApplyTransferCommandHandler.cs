using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Handlers;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class ApplyTransferCommandHandler(
        ILogger<ApplyTransferCommandHandler> logger,
        IMapper mapper,
        IDateTimeProvider dateTimeProvider,
        IExectutionStrategyHandler<IKafkaProducer<string, string>> kafkaHandler,
        IOutboxPaymentStatusService outboxPaymentStatusService)
        : BasePaymentCommandHandler(
            logger,
            mapper,
            dateTimeProvider,
            kafkaHandler,
            outboxPaymentStatusService),
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
