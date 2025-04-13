using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Infrastructure.Factories;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Commands.Handlers
{
    internal abstract class BasePaymentCommandHandler(
        ILogger logger,
        IMapper mapper,
        IFireAndForgetHandler<IKafkaProducer<string, string>> fireAndForgetHandler)
    {
        protected Task<Result<Guid>> HandleAsync<T>(
            T request,
            CancellationToken cancellationToken)
        {
            var paymentEvent = mapper.Map<PaymentOperationEvent>(request);

            var paymentAccountId = paymentEvent.Payload.PaymentAccountId;

            var paymentMessageResult = PaymentEventToMessageConverter.Convert(paymentEvent);

            fireAndForgetHandler.Execute(async producer =>
            {
                var topic = new SubscriptionTopic
                {
                    Title = KafkaTopicTitleFactory.GetPaymentAccountTopic(paymentAccountId),
                    ConsumerType = ConsumerTypes.PaymentOperations
                };

                try
                {
                    var message = paymentMessageResult.Payload;
                    var topicTitle = topic.Title?.ToLower(System.Globalization.CultureInfo.CurrentCulture);
                    await producer.ProduceAsync(topicTitle, message, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during producing message event. The error: {Message}", ex.Message);
                }
            });

            return Task.FromResult(Result<Guid>.Succeeded(paymentEvent.Payload.Key));
        }
    }
}
