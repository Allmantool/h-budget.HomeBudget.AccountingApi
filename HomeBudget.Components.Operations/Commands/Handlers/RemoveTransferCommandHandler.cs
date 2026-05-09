using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;

using MediatR;

using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class RemoveTransferCommandHandler(
        IMapper mapper,
        IDateTimeProvider dateTimeProvider,
        IOutboxPaymentStatusService outboxPaymentStatusService)
        : BasePaymentCommandHandler(
            mapper,
            dateTimeProvider,
            outboxPaymentStatusService),
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
