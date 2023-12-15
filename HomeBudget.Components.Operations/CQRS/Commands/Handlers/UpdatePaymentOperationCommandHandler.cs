using System;
using System.Linq;
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
    internal class UpdatePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
        : IRequestHandler<UpdatePaymentOperationCommand, Result<Guid>>
    {
        public Task<Result<Guid>> Handle(UpdatePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            var paymentAccountId = request.OperationForUpdate.PaymentAccountId;

            var paymentOperationEvent = mapper.Map<PaymentOperationEvent>(request);

            var eventsForAccount = MockOperationEventsStore.EventsForAccount(paymentAccountId).ToList();

            MockOperationEventsStore.SetState(paymentAccountId, eventsForAccount.Append(paymentOperationEvent));

            var upToDateBalanceResult = paymentOperationsHistoryService.SyncHistory(paymentAccountId);

            sender.Send(
                new UpdatePaymentAccountBalanceCommand(
                    paymentAccountId,
                    upToDateBalanceResult.Payload),
                cancellationToken);

            return Task.FromResult(new Result<Guid>(paymentOperationEvent.Payload.Key));
        }
    }
}
