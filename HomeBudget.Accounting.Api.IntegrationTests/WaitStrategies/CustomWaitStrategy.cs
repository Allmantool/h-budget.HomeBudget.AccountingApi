using System;
using System.Threading.Tasks;

using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;

internal class CustomWaitStrategy : IWaitUntil
{
    private readonly TimeSpan _timeout;
    private readonly string[] _readyIndicators =
    {
        "ready", "started", "listening", "healthy"
    };

    public CustomWaitStrategy(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public async Task<bool> UntilAsync(IContainer container)
    {
        var deadline = DateTime.UtcNow + _timeout;
        var delay = TimeSpan.FromMilliseconds(300);

        int lastLogLength = 0;

        while (DateTime.UtcNow < deadline)
        {
            if (container.Health == TestcontainersHealthStatus.Healthy)
            {
                return true;
            }

            var (isReady, updatedLogLength) = await IsContainerReadyFromLogsAsync(container, lastLogLength);

            lastLogLength = updatedLogLength;

            if (isReady)
            {
                return true;
            }

            await Task.Delay(delay);
            delay = TimeSpan.FromMilliseconds(
                Math.Min(
                    delay.TotalMilliseconds * 1.7,
                    BaseTestContainerOptions.WaitStrategyInSeconds * 1000)
            );
        }

        return false;
    }

    private async Task<(bool isReady, int updatedLength)> IsContainerReadyFromLogsAsync(IContainer container, int lastLogLength)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(container.Id))
            {
                return (false, lastLogLength);
            }

            var logs = await container.GetLogsAsync();

            var stdout = logs.Stdout ?? string.Empty;
            var stderr = logs.Stderr ?? string.Empty;
            var combined = stdout + stderr;

            if (combined.Length <= lastLogLength)
            {
                return (false, lastLogLength);
            }

            var newSegment = combined[lastLogLength..];
            var updatedLength = combined.Length;

            foreach (var indicator in _readyIndicators)
            {
                if (newSegment.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, updatedLength);
                }
            }

            return (false, updatedLength);
        }
        catch
        {
            return (false, lastLogLength);
        }
    }
}
