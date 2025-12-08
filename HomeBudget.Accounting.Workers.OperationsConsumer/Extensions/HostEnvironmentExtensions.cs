using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Extensions
{
    internal static class HostEnvironmentExtensions
    {
        public static bool IsUnderDevelopment(this IHostEnvironment environment)
            => environment.IsDevelopment() || environment.IsEnvironment(HostEnvironments.Docker);
    }
}
