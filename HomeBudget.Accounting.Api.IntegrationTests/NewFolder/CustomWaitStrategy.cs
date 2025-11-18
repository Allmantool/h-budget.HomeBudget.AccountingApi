using System;
using System.Threading.Tasks;

using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

internal class CustomWaitStrategy : IWaitUntil
{
    private readonly TimeSpan _timeout;

    public CustomWaitStrategy(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public async Task<bool> UntilAsync(IContainer container)
    {
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < _timeout)
        {
            if (await CheckIfContainerIsReadyAsync(container))
            {
                return true;
            }

            // Avoid hammering logs too fast
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return false;
    }

    private static async Task<bool> CheckIfContainerIsReadyAsync(IContainer container)
    {
        try
        {
            var logs = await container.GetLogsAsync();

            return logs.Stdout.Contains("started", StringComparison.OrdinalIgnoreCase)
                || logs.Stderr.Contains("started", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            return false;
        }
    }
}
