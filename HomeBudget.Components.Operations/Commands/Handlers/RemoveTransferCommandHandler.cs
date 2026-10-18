using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;

using MediatR;

using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class RemoveTransferCommandHandler(
        IMapper mapper,
        IDateTimeProvider dateTimeProvider,
        IOutboxPaymentStatusService outboxPaymentStatusService,
        IActiveLedgerGenerationResolver activeGenerationResolver = null)
        : BasePaymentCommandHandler(
            mapper,
            dateTimeProvider,
            outboxPaymentStatusService,
            activeGenerationResolver),
            IRequestHandler<RemoveTransferCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(RemoveTransferCommand request, CancellationToken cancellationToken)
        {
            var paymentOperations = request.PaymentOperations;

            var childCommands = paymentOperations.Select(op => new RemovePaymentOperationCommand(op)
            {
                CorrelationId = request.CorrelationId,
                ExpectedGeneration = request.ExpectedGeneration
            });

            await WriteTransferRecordsAsync(childCommands, cancellationToken);

            return Result<Guid>.Succeeded(request.Key);
        }
    }
}
