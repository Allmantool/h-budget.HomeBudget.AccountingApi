using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Infrastructure.Constants;
using HomeBudget.Accounting.Infrastructure.Consumers;
using HomeBudget.Components.Accounts.Handlers;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Options;

namespace HomeBudget.Components.Accounts.Consumers
{
    internal class AccountOperationsConsumer(
        ILogger<AccountOperationsConsumer> logger,
        IAccountOperationsHandler accountOperationsHandler,
        IOptions<KafkaOptions> kafkaOptions)
        : BaseKafkaConsumer<string, string>(EnrichConsumerOptions(kafkaOptions.Value), logger)
    {
        private static KafkaOptions EnrichConsumerOptions(KafkaOptions options)
        {
            var consumerSettings = options.ConsumerSettings;

            if (consumerSettings == null)
            {
                return options;
            }

            consumerSettings.GroupId = "accounting.accounts.group";
            consumerSettings.ClientId = $"{consumerSettings.GroupId}_{Guid.NewGuid()}";

            consumerSettings.EnableAutoCommit = false;

            return options;
        }

        public override Task ConsumeAsync(CancellationToken cancellationToken)
        {
            try
            {
                return ConsumeAsync(
                    async payload =>
                    {
                        try
                        {
                            var message = payload.Message;

                            if (message == null || string.IsNullOrWhiteSpace(message.Value))
                            {
                                return;
                            }

                            message.Headers.Add(KafkaMessageHeaders.ProcessedAt, Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")));

                            logger.LogInformation("Account message has been consumed: {MessageId}", message.Key);

                            var accountEvent = JsonSerializer.Deserialize<AccountOperationEvent>(message.Value);

                            accountEvent.Metadata.Add(EventMetadataKeys.FromMessage, message.Key);

                            await accountOperationsHandler.HandleAsync(accountEvent, cancellationToken);
                        }
                        catch (JsonException ex)
                        {
                            logger.LogError(ex, "Failed to deserialize message: {Message}. Error: {Error}", payload.Message.Value, ex.Message);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "{PaymentOperationsConsumer} failed to consume with error: {Exception}", nameof(AccountOperationsConsumer), ex.Message);
                        }
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}
