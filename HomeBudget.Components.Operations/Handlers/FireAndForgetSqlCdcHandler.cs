using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Infrastructure.Data.Interfaces;
using HomeBudget.Core.Handlers;

namespace HomeBudget.Components.Operations.Handlers
{
    internal class FireAndForgetSqlCdcHandler(IServiceScopeFactory serviceScopeFactory)
        : IExectutionStrategyHandler<IBaseWriteRepository>
    {
        public async Task ExecuteAndWaitAsync(Func<IBaseWriteRepository, Task> callback)
        {
            await ExecuteInternalAsync(callback);
        }

        public void ExecuteFireAndForget(Func<IBaseWriteRepository, Task> callback)
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

        private async Task ExecuteInternalAsync(Func<IBaseWriteRepository, Task> callback)
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();

            var kafkaProducer = scope.ServiceProvider
                .GetRequiredService<IBaseWriteRepository>();

            await callback(kafkaProducer);
        }
    }
}
