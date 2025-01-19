using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal abstract class BasePaymentCommandHandler(
        IMapper mapper,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
    {
        protected Task<Result<Guid>> HandleAsync<T>(
            T request,
            CancellationToken cancellationToken)
        {
            var paymentEvent = mapper.Map<PaymentOperationEvent>(request);

            var paymentAccountId = paymentEvent.Payload.PaymentAccountId;

            var paymentMessage = PaymentEventToMessageConverter.Convert(paymentEvent);

            // TODO: Verify that all required information has been sent.
            fireAndForgetHandler.Execute(async producer =>
            {
                var topic = new SubscriptionTopic
                {
                    Title = $"payment-account-{paymentAccountId}",
                    ConsumerType = ConsumerTypes.PaymentOperations
                };

                await producer.ProduceAsync(topic.Title, paymentMessage.Payload, cancellationToken);
            });

            return Task.FromResult(Result<Guid>.Succeeded(paymentEvent.Payload.Key));
        }
    }
}
