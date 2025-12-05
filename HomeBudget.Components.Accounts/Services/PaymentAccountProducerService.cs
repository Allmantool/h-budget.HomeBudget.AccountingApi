using System;
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

                var headers = new Headers
                {
                    new Header(KafkaMessageHeaders.Type, Encoding.UTF8.GetBytes(nameof(AccountRecord))),
                    new Header(KafkaMessageHeaders.Version, Encoding.UTF8.GetBytes("1.0")),
                    new Header(KafkaMessageHeaders.Source, Encoding.UTF8.GetBytes(nameof(PaymentAccountProducerService))),
                    new Header(KafkaMessageHeaders.EnvelopId, Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                    new Header(KafkaMessageHeaders.OccuredOn, Encoding.UTF8.GetBytes(enquiredAt.ToString("O"))),
                };

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
