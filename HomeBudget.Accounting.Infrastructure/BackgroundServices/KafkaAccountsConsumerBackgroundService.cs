using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Constants;
using HomeBudget.Core.Exceptions;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Infrastructure.BackgroundServices
{
    internal class KafkaAccountsConsumerBackgroundService(
        ILogger<KafkaAccountsConsumerBackgroundService> logger,
        IOptions<KafkaOptions> options,
        Channel<AccountRecord> paymentAccountsChannel,
        IConsumerService consumerService,
        IServiceScopeFactory serviceScopeFactory)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // TODO: create 1 account consumer for 'accounting.accounts' topic
                    if (ConsumersStore.Consumers.TryGetValue(BaseTopics.AccountingAccounts, out var consumers) && consumers.IsNullOrEmpty())
                    {
                        consumerService.CreateAndSubscribe(new SubscriptionTopic
                        {
                            ConsumerType = ConsumerTypes.AccountOperations,
                            Title = BaseTopics.AccountingAccounts
                        });
                    }

                    // TOOD: health monitoring, re-create consumer if failed
                    if (paymentAccountsChannel.Reader.Completion.IsCompleted)
                    {
                        logger.LogWarning("The accounts channel has been completed. No more topics will be processed.");
                        break;
                    }

                    // Send message
                    await foreach (var paymentAccount in paymentAccountsChannel.Reader.ReadAllAsync(stoppingToken))
                    {
                        logger.LogInformation("Send new account related message. Account id: {AccountId}", paymentAccount.Id);

                        using var scope = serviceScopeFactory.CreateScope();

                        var paymentAccountProcessor = scope.ServiceProvider.GetRequiredService<IPaymentAccountProducerService>();

                        _ = Task.Run(
                            () => paymentAccountProcessor.SendAsync(paymentAccount, stoppingToken),
                            stoppingToken);
                    }
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogError(ex, "Shutting down {Service}...", nameof(KafkaAccountsConsumerBackgroundService));
                }
                catch (Exception ex)
                {
                    logger.LogError(
                    ex,
                    "Unexpected error in {Service}. Restarting in {Delay} seconds...",
                    nameof(KafkaAccountsConsumerBackgroundService),
                    options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds);

                    await Task.Delay(TimeSpan.FromSeconds(options.Value.ConsumerSettings.ConsumerCircuitBreakerDelayInSeconds), stoppingToken);
                }
            }
        }
    }
}
