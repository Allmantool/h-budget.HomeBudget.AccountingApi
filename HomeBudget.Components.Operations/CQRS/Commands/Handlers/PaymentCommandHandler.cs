using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Accounts.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.CQRS.Commands.Handlers
{
    internal abstract class BasePaymentCommandHandler(
        IMapper mapper,
        ISender sender,
        IKafkaDependentProducer<string, string> producer,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
    {
        protected async Task<Result<Guid>> HandleAsync<T>(
            T request,
            CancellationToken cancellationToken)
        {
            var paymentSavedEvent = mapper.Map<PaymentOperationEvent>(request);

            var result = await producer.ProduceAsync(
                nameof(paymentSavedEvent),
                PaymentEventToMessageConverter.Convert(paymentSavedEvent),
                cancellationToken
            );

            await operationsDeliveryHandler.HandleAsync(result, cancellationToken);

            var upToDateBalanceResult = await paymentOperationsHistoryService.SyncHistoryAsync(paymentSavedEvent.Payload.PaymentAccountId);

            await sender.Send(
                new UpdatePaymentAccountBalanceCommand(
                    paymentSavedEvent.Payload.PaymentAccountId,
                    upToDateBalanceResult.Payload),
                cancellationToken);

            return new Result<Guid>(paymentSavedEvent.Payload.Key);
        }
    }
}
