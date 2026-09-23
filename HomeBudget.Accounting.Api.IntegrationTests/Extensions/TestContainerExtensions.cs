using System;
using System.Threading.Tasks;

using Docker.DotNet;
using DotNet.Testcontainers.Containers;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class TestContainerExtensions
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

        public static async Task SafeStartWithRetryAsync(
            this IContainer container,
            int maxRetries = 3,
            int baseDelaySeconds = 5,
            bool swallowBusyError = false)
        {
            ArgumentNullException.ThrowIfNull(container);

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    await container.StartAsync();
                    return;
                }
                catch (DockerApiException ex) when (IsRetryableDockerError(ex) && attempt < maxRetries)
                {
                    LogRetry("Docker reported a transient writable-layer error", ex, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(baseDelaySeconds * attempt));
                }
                catch (DockerContainerNotFoundException ex) when (attempt < maxRetries)
                {
                    LogRetry("Container not found (likely crashed on startup)", ex, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(baseDelaySeconds * (attempt + 1)));
                }
                catch (DockerApiException ex) when (
                    ex.Message.Contains("device or resource busy", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[TESTCONTAINERS] Resource busy detected!");
                    if (swallowBusyError)
                    {
                        return;
                    }

                    throw;
                }
            }

            await container.StartAsync();
        }

        private static void LogRetry(string title, Exception ex, int attempt)
        {
            Console.WriteLine($"[TESTCONTAINERS RETRY] Attempt #{attempt + 1}");
            Console.WriteLine($"Reason: {title}");
            Console.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
        }

        private static bool IsRetryableDockerError(DockerApiException ex)
            => ex.Message.Contains("RWLayer", StringComparison.OrdinalIgnoreCase);
    }
}
