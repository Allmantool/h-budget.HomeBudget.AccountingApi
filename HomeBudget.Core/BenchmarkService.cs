using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Core
{
    public static class BenchmarkService
    {
        public static async Task<T> WithBenchmarkAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            ILogger logger,
            object context)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            logger.LogDebug("{OperationName} started with context: {@Context}", operationName, context);

            try
            {
                var result = await operation();
                stopwatch.Stop();
                logger.LogInformation(
                    "{OperationName} completed in {ElapsedMilliseconds} ms with context: {@Context}",
                    operationName,
                    stopwatch.ElapsedMilliseconds,
                    context);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                logger.LogError(
                    ex,
                    "{OperationName} failed after {ElapsedMilliseconds} ms with context: {@Context}",
                    operationName,
                    stopwatch.ElapsedMilliseconds,
                    context);

                throw;
            }
        }

        public static async Task WithBenchmarkAsync(
            Func<Task> operation,
            string operationName,
            ILogger logger,
            object context)
        {
            await WithBenchmarkAsync(
                async () =>
                {
                    await operation();
                    return true;
                },
                operationName,
                logger,
                context);
        }
    }
}
