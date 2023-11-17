using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Operations.Factories;

namespace HomeBudget.Components.Operations.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection RegisterOperationsIoCDependency(
            this IServiceCollection services)
        {
            return services
                .AddScoped<IOperationFactory, OperationFactory>();
        }
    }
}
