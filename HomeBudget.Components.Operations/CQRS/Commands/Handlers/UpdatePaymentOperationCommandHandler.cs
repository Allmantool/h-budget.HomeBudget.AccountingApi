using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Accounts.CQRS.Commands.Models;
using HomeBudget.Components.Operations.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.CQRS.Commands.Handlers
{
    internal class UpdatePaymentOperationCommandHandler(
        IMapper mapper,
        ISender sender,
        IEventStoreDbClient<PaymentOperationEvent> eventStoreDbClient,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
        : IRequestHandler<UpdatePaymentOperationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(UpdatePaymentOperationCommand request, CancellationToken cancellationToken)
        {
            var paymentAccountId = request.OperationForUpdate.PaymentAccountId;

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
