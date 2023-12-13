using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Operations.Factories;
using HomeBudget.Components.Operations.Services;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterOperationsIoCDependency(
            this IServiceCollection services)
        {
            return services
                .AddScoped<IOperationFactory, OperationFactory>()
                .AddScoped<IPaymentOperationsService, PaymentOperationsService>()
                .AddScoped<IPaymentOperationsHistoryService, PaymentOperationsHistoryService>()
                .AddMediatR(configuration =>
                {
                    configuration.RegisterServicesFromAssembly(typeof(DependencyRegistrations).Assembly);
                });
        }
    }
}
