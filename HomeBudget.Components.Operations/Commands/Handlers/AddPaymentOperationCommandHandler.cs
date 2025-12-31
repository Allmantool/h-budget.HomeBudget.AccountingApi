using System;
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
    internal class AddPaymentOperationCommandHandler(
        ILogger<AddPaymentOperationCommandHandler> logger,
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
        IRequestHandler<AddPaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(AddPaymentOperationCommand command, CancellationToken cancellationToken)
        {
            return await HandleAsync(command, cancellationToken);
        }
    }
}
