using System;
using System.Threading.Tasks;

using DotNet.Testcontainers.Containers;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class TestContainerExtentions
    {
        public static async Task DumpContainerLogsSafelyAsync(this IContainer container, string name)
        {
            if (container is null)
            {
                Console.WriteLine($"{name} container is null — no logs.");
                return;
            }

            try
            {
                var logs = await container.GetLogsAsync();
                Console.WriteLine($"{name} container logs:");

                Console.WriteLine("--- STDOUT ---");
                Console.WriteLine(logs.Stdout ?? "<empty>");

                Console.WriteLine("--- STDERR ---");
                Console.WriteLine(logs.Stderr ?? "<empty>");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{name} logs unavailable: {ex.Message}");
            }
        }

        public static async Task SafeStartContainerAsync(
            this IContainer container,
            bool swallowBusyError = false)
        {
            try
            {
                await container.StartAsync();
            }
            catch (Exception ex)
            {
                if (swallowBusyError &&
                    ex.Message.Contains("testcontainers.sh: device or resource busy", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Kafka container busy warning ignored.");
                    return;
                }

                throw;
            }
        }
    }
}
