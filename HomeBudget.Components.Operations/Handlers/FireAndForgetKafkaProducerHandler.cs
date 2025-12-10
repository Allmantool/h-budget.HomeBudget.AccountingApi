using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Core.Handlers;

namespace HomeBudget.Components.Operations.Handlers
{
    internal class FireAndForgetKafkaProducerHandler(IServiceScopeFactory serviceScopeFactory)
        : IExectutionStrategyHandler<IKafkaProducer<string, string>>
    {
        public void ExecuteFireAndForget(Func<IKafkaProducer<string, string>, Task> callback)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteInternalAsync(callback);
                }
                catch
                {
                }
            });
        }

        public async Task ExecuteAndWaitAsync(Func<IKafkaProducer<string, string>, Task> callback)
        {
            await ExecuteInternalAsync(callback);
        }

        private async Task ExecuteInternalAsync(Func<IKafkaProducer<string, string>, Task> callback)
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();

            var kafkaProducer = scope.ServiceProvider
                .GetRequiredService<IKafkaProducer<string, string>>();

            await callback(kafkaProducer);
        }
    }
}
