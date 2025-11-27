using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Api.Extensions
{
    internal static class HostEnvironmentExtensions
    {
        public static bool IsUnderDevelopment(this IWebHostEnvironment environment)
            => environment.IsDevelopment() || environment.IsEnvironment(HostEnvironments.Docker);

        public static bool IsUnderDevelopment(this IHostEnvironment environment)
            => environment.IsDevelopment() || environment.IsEnvironment(HostEnvironments.Docker);

        public static bool IsIntegrationTesting(this IWebHostEnvironment environment)
            => environment.IsEnvironment(HostEnvironments.Integration);
    }
}
