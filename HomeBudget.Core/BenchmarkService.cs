using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace HomeBudget.Core
{
    public static class BenchmarkService
    {
        private static readonly Action<ILogger, string, object, Exception> _operationStarted =
            LoggerMessage.Define<string, object>(
                LogLevel.Debug,
                new EventId(1, "OperationStarted"),
                "Operation '{OperationName}' started with context: {@Context}");

        private static readonly Action<ILogger, string, int, int, int, object, Exception> _operationCompleted =
            LoggerMessage.Define<string, int, int, int, object>(
                LogLevel.Information,
                new EventId(2, "OperationCompleted"),
                "Operation '{OperationName}' completed in '{Hours}:{Minutes}:{Seconds}' with context: {@Context}");

        private static readonly Action<ILogger, string, int, int, int, object, Exception> _operationFailed =
            LoggerMessage.Define<string, int, int, int, object>(
                LogLevel.Error,
                new EventId(3, "OperationFailed"),
                "Operation '{OperationName}' failed after '{Hours}:{Minutes}:{Seconds}' with context: {@Context}");

        public static async Task<T> WithBenchmarkAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            ILogger logger,
            object context)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            LogOperationStarted(logger, operationName, context);

            try
            {
                var result = await operation();
                stopwatch.Stop();
                LogOperationCompleted(logger, operationName, stopwatch.Elapsed, context);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogOperationFailed(logger, operationName, stopwatch.Elapsed, context, ex);
                throw;
            }
        }

        private static void LogOperationStarted(ILogger logger, string operationName, object context)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _operationStarted(logger, operationName, context, null);
            }
        }

        private static void LogOperationCompleted(
            ILogger logger,
            string operationName,
            TimeSpan elapsed,
            object context)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                _operationCompleted(
                    logger,
                    operationName,
                    elapsed.Hours,
                    elapsed.Minutes,
                    elapsed.Seconds,
                    context,
                    null);
            }
        }

        private static void LogOperationFailed(
            ILogger logger,
            string operationName,
            TimeSpan elapsed,
            object context,
            Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                _operationFailed(
                    logger,
                    operationName,
                    elapsed.Hours,
                    elapsed.Minutes,
                    elapsed.Seconds,
                    context,
                    ex);
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
