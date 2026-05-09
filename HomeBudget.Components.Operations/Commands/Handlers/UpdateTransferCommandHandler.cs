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
    internal class UpdateTransferCommandHandler(
        IMapper mapper,
        IDateTimeProvider dateTimeProvider,
        IOutboxPaymentStatusService outboxPaymentStatusService)
        : BasePaymentCommandHandler(
            mapper,
            dateTimeProvider,
            outboxPaymentStatusService),
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
