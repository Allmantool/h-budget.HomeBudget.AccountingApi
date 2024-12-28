using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Extensions;
using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal class UpdatePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IPaymentsHistoryDocumentsClient historyDocumentsClient,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
        : BasePaymentCommandHandler(
                mapper,
                operationsDeliveryHandler,
                fireAndForgetHandler),
            IRequestHandler<UpdatePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(UpdatePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            var operationForUpdate = request.OperationForUpdate;
            var operationId = operationForUpdate.Key;
            var accountId = operationForUpdate.PaymentAccountId;
            var operationBeforeUpdate = await historyDocumentsClient.GetByIdAsync(accountId, operationId);

            if (operationBeforeUpdate != null)
            {
                var updateOperationIdentifier = request.OperationForUpdate.OperationDay.ToFinancialPeriod();
                var deleteOperationIdentifier = operationBeforeUpdate.Payload.Record.OperationDay.ToFinancialPeriod();

                var ifFFinancialPeriodHasBeenChanged = updateOperationIdentifier.StartDate != deleteOperationIdentifier.StartDate;

                if (ifFFinancialPeriodHasBeenChanged)
                {
                    var removeCommand = new RemovePaymentOperationCommand(operationBeforeUpdate.Payload.Record);

                    await sender.Send(removeCommand, cancellationToken);
                }
            }

            return await HandleAsync(request, cancellationToken);
        }
    }
}
