using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.CQRS.Commands.Models;
using HomeBudget.Components.Operations.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.CQRS.Commands.Handlers
{
    internal class RemovePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
        : IRequestHandler<RemovePaymentOperationCommand, Result<Guid>>
    {
        public Task<Result<Guid>> Handle(RemovePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            var paymentOperationEvent = mapper.Map<PaymentOperationEvent>(request);

            MockOperationEventsStore.Events.Add(paymentOperationEvent);

            var upToDateBalanceResult = paymentOperationsHistoryService.SyncHistory(request.OperationForDelete.PaymentAccountId);

            sender.Send(
                new UpdatePaymentAccountBalanceCommand(
                    request.OperationForDelete.PaymentAccountId,
                    upToDateBalanceResult.Payload),
                cancellationToken);

            return Task.FromResult(new Result<Guid>(paymentOperationEvent.PaymentOperationId));
        }
    }
}
