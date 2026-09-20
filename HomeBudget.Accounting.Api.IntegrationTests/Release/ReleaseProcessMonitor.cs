using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Api.IntegrationTests.Release
{
    internal static class ReleaseProcessMonitor
    {
        public static Task WaitForExitAsync(
            CapturedReleaseProcess process,
            Func<long> readProgressMarker,
            TimeSpan stallTimeout,
            TimeSpan pollInterval,
            CancellationToken token)
        {
            if (stallTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(stallTimeout));
            }

            if (pollInterval <= TimeSpan.Zero || pollInterval >= stallTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(pollInterval));
            }

            return WaitForExitCoreAsync(process, readProgressMarker, stallTimeout, pollInterval, token);
        }

        private static async Task WaitForExitCoreAsync(
            CapturedReleaseProcess process,
            Func<long> readProgressMarker,
            TimeSpan stallTimeout,
            TimeSpan pollInterval,
            CancellationToken token)
        {
            var marker = readProgressMarker();
            var timeSinceProgress = Stopwatch.StartNew();
            try
            {
                while (!process.HasExited)
                {
                    await Task.Delay(pollInterval, token);
                    var current = readProgressMarker();
                    if (current != marker)
                    {
                        marker = current;
                        timeSinceProgress.Restart();
                    }

                    if (timeSinceProgress.Elapsed >= stallTimeout)
                    {
                        await process.StopAsync();
                        throw new TimeoutException(
                            $"Process made no durable progress for {stallTimeout}. Last marker: {marker}.");
                    }
                }

                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await process.StopAsync();
                throw;
            }
        }
    }
}
