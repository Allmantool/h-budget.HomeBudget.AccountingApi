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
            var paymentEvent = mapper.Map<PaymentOperationEvent>(request);

            var paymentAccountId = paymentEvent.Payload.PaymentAccountId;

            var paymentMessageConversionResult = PaymentEventToMessageConverter.Convert(paymentEvent);

            var result = await producer.ProduceAsync(
                paymentAccountId.ToString(),
                paymentMessageConversionResult.Payload,
                cancellationToken
            );

            await operationsDeliveryHandler.HandleAsync(result, cancellationToken);

            var upToDateBalanceResult = await paymentOperationsHistoryService.SyncHistoryAsync(paymentAccountId);

            await sender.Send(
                new UpdatePaymentAccountBalanceCommand(
                    paymentAccountId,
                    upToDateBalanceResult.Payload),
                cancellationToken);

            return new Result<Guid>(paymentEvent.Payload.Key);
        }
    }
}
