using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Observability;

namespace HomeBudget.Components.Accounts.Services
{
    internal class PaymentAccountProducerService(
        ILogger<PaymentAccountProducerService> logger,
        IDateTimeProvider dateTimeProvider,
        IKafkaProducer<string, string> accountProducer)
        : IPaymentAccountProducerService
    {
        public async Task SendAsync(AccountRecord accountRecord, CancellationToken token)
        {
            try
            {
                var accountEvent = new AccountOperationEvent
                {
                    Payload = new PaymentAccount
                    {
                        Balance = 0,
                        Key = accountRecord.Id,
                        InitialBalance = 0
                    }
                };

                var enquiredAt = dateTimeProvider.GetNowUtc();
                var messagePayload = JsonSerializer.Serialize(accountEvent);
                var messageId = Guid.NewGuid().ToString("N");

                var headers = new Headers
                {
                    new Header(KafkaMessageHeaders.Type, Encoding.UTF8.GetBytes(nameof(AccountRecord))),
                    new Header(KafkaMessageHeaders.Version, Encoding.UTF8.GetBytes("1.0")),
                    new Header(KafkaMessageHeaders.Source, Encoding.UTF8.GetBytes(nameof(PaymentAccountProducerService))),
                    new Header(KafkaMessageHeaders.EnvelopId, Encoding.UTF8.GetBytes(messageId)),
                    new Header(KafkaMessageHeaders.OccuredOn, Encoding.UTF8.GetBytes(enquiredAt.ToString("O"))),
                };

                if (Activity.Current is not null)
                {
                    var propagationCarrier = TraceContextPropagation.Capture(Activity.Current);

                    if (propagationCarrier.TryGetValue(TraceContextPropagation.TraceParent, out var traceParent))
                    {
                        headers.Add(KafkaMessageHeaders.Traceparent, Encoding.UTF8.GetBytes(traceParent));
                    }

                    if (propagationCarrier.TryGetValue(TraceContextPropagation.TraceState, out var traceState))
                    {
                        headers.Add(KafkaMessageHeaders.Tracestate, Encoding.UTF8.GetBytes(traceState));
                    }

                    if (propagationCarrier.TryGetValue(TraceContextPropagation.Baggage, out var baggage))
                    {
                        headers.Add(KafkaMessageHeaders.Baggage, Encoding.UTF8.GetBytes(baggage));
                    }

                    headers.Add(KafkaMessageHeaders.TraceId, Encoding.UTF8.GetBytes(Activity.Current.TraceId.ToString()));
                    headers.Add(KafkaMessageHeaders.CausationId, Encoding.UTF8.GetBytes(Activity.Current.SpanId.ToString()));
                }

                var correlationId = Activity.Current?.GetBaggageItem(ActivityTags.CorrelationId);
                if (!string.IsNullOrWhiteSpace(correlationId))
                {
                    headers.Add(KafkaMessageHeaders.CorrelationId, Encoding.UTF8.GetBytes(correlationId));
                }

                headers.Add(KafkaMessageHeaders.MessageId, Encoding.UTF8.GetBytes(messageId));

                var message = new Message<string, string>
                {
                    Key = $"{accountRecord.Id}",
                    Value = messagePayload,
                    Timestamp = new Timestamp(enquiredAt),
                    Headers = headers
                };

                await accountProducer.ProduceAsync(BaseTopics.AccountingAccounts, message, token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send message to '{Topic}': {ErrorMessage}", BaseTopics.AccountingAccounts, ex.Message);
            }
        }
    }
}

