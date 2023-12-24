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
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;

namespace HomeBudget.Components.Operations.CQRS.Commands.Handlers
{
    internal class RemovePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
        : IRequestHandler<RemovePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(RemovePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            var paymentAccountId = request.OperationForDelete.PaymentAccountId;

            var paymentOperationEvent = mapper.Map<PaymentOperationEvent>(request);

            await eventStoreDbClient.SendAsync(
                paymentOperationEvent,
                token: cancellationToken);

            var upToDateBalanceResult = await paymentOperationsHistoryService.SyncHistoryAsync(paymentAccountId);

            await sender.Send(
                 new UpdatePaymentAccountBalanceCommand(
                     paymentAccountId,
                     upToDateBalanceResult.Payload),
                 cancellationToken);

            return new Result<Guid>(paymentOperationEvent.Payload.Key);
        }
    }
}
