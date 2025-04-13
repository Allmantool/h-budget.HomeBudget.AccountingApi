using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Handlers;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;

namespace HomeBudget.Components.Operations.Handlers
{
    internal class FireAndForgetKafkaProducerHandler(IServiceScopeFactory serviceScopeFactory)
        : IFireAndForgetHandler<IKafkaProducer<string, string>>
    {
        public void Execute(Func<IKafkaProducer<string, string>, Task> callback)
        {
            Task.Run(async () =>
            {
                using var scope = serviceScopeFactory.CreateAsyncScope();
                var kafkaProducer = scope.ServiceProvider.GetRequiredService<IKafkaProducer<string, string>>();

                await callback.Invoke(kafkaProducer);
            });
        }
    }
}
