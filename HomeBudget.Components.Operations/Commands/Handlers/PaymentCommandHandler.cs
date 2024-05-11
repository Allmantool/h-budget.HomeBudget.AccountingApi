using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using MediatR;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Accounts.Commands.Models;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal abstract class BasePaymentCommandHandler(
        IMapper mapper,
        ISender sender,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler,
        IPaymentOperationsHistoryService paymentOperationsHistoryService)
    {
        protected async Task<Result<Guid>> HandleAsync<T>(
            T request,
            CancellationToken cancellationToken)
        {
            var paymentEvent = mapper.Map<PaymentOperationEvent>(request);

            var paymentAccountId = paymentEvent.Payload.PaymentAccountId;

            var paymentMessage = PaymentEventToMessageConverter.Convert(paymentEvent);

            fireAndForgetHandler.Execute(async producer => await producer.ProduceAsync(paymentAccountId.ToString(), paymentMessage.Payload, cancellationToken));

            await operationsDeliveryHandler.HandleAsync(paymentEvent, cancellationToken);

            var upToDateBalanceResult = await paymentOperationsHistoryService.SyncHistoryAsync(paymentAccountId);

            await sender.Send(
                new UpdatePaymentAccountBalanceCommand(
                    paymentAccountId,
                    upToDateBalanceResult.Payload),
                cancellationToken);

            return Result<Guid>.Succeeded(paymentEvent.Payload.Key);
        }
    }
}
