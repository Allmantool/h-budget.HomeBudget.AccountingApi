using System;
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
    internal class AddPaymentOperationCommandHandler(
        IMapper mapper,
        IDateTimeProvider dateTimeProvider,
        IOutboxPaymentStatusService outboxPaymentStatusService,
        IActiveLedgerGenerationResolver activeGenerationResolver = null)
        : BasePaymentCommandHandler(
                mapper,
                dateTimeProvider,
                outboxPaymentStatusService,
                activeGenerationResolver),
        IRequestHandler<AddPaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(AddPaymentOperationCommand command, CancellationToken cancellationToken)
        {
            return await HandleAsync(command, cancellationToken);
        }
    }
}
