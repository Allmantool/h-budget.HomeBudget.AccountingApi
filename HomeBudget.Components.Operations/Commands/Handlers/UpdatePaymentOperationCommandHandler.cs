using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class UpdatePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IDateTimeProvider dateTimeProvider,
        IPaymentsHistoryDocumentsClient historyDocumentsClient,
        IOutboxPaymentStatusService outboxPaymentStatusService)
        : BasePaymentCommandHandler(
            mapper,
            dateTimeProvider,
            outboxPaymentStatusService),
        IRequestHandler<UpdatePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(UpdatePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            var operationForUpdate = request.OperationForUpdate;
            var operationId = operationForUpdate.Key;
            var accountId = operationForUpdate.PaymentAccountId;
            var operationBeforeUpdate = await historyDocumentsClient.GetByIdAsync(accountId, operationId);

            if (operationBeforeUpdate == null)
            {
                return await HandleAsync(request, cancellationToken);
            }

            var updateOperationIdentifier = request.OperationForUpdate.OperationDay.ToFinancialPeriod();
            var deleteOperationIdentifier = operationBeforeUpdate.Payload.Record.OperationDay.ToFinancialPeriod();

            var ifFFinancialPeriodHasBeenChanged = updateOperationIdentifier.StartDate != deleteOperationIdentifier.StartDate;

            if (ifFFinancialPeriodHasBeenChanged)
            {
                var removeCommand = new RemovePaymentOperationCommand(operationBeforeUpdate.Payload.Record)
                {
                    CommandContext = CreateRelocationRemoveContext(request, accountId, operationId)
                };

                await sender.Send(removeCommand, cancellationToken);
            }

            return await HandleAsync(request, cancellationToken);
        }

        private static PaymentCommandContext CreateRelocationRemoveContext(
            UpdatePaymentOperationCommand request,
            Guid paymentAccountId,
            Guid operationId)
        {
            var context = request.CommandContext;
            if (context is null)
            {
                return null;
            }

            return new PaymentCommandContext
            {
                IdempotencyKeyHash = PaymentCommandFingerprint.CreateDerivedIdempotencyKeyHash(
                    context.IdempotencyKeyHash,
                    "relocation-remove"),
                RequestFingerprint = PaymentCommandFingerprint.CreateDelete(
                    PaymentCommandTypes.Delete,
                    paymentAccountId,
                    operationId),
                CommandType = PaymentCommandTypes.Delete
            };
        }
    }
}
