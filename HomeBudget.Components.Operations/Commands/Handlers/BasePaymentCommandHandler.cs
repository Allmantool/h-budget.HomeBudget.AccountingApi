using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Handlers;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal abstract class BasePaymentCommandHandler(
        IMapper mapper,
        IPaymentOperationsDeliveryHandler operationsDeliveryHandler,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
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

            return Result<Guid>.Succeeded(paymentEvent.Payload.Key);
        }
    }
}
