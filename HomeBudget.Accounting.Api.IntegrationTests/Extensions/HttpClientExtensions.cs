using System;
using System.Threading;
using System.Threading.Tasks;

using RestSharp;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class HttpClientExtensions
    {
        private const int BaseDelayUntilEventSourceReachASyncStateMs = 300;

        public static async Task<RestResponse<T>> ExecuteWithDelayAsync<T>(
            this IRestClient client,
            RestRequest request,
            int executionDelayBeforeInMs = BaseDelayUntilEventSourceReachASyncStateMs,
            int executionDelayAfterInMs = BaseDelayUntilEventSourceReachASyncStateMs,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);

            return await ExecuteWithDelayInternalAsync(
                async () => await client.ExecuteAsync<T>(request, cancellationToken),
                executionDelayBeforeInMs,
                executionDelayAfterInMs,
                cancellationToken
            );
        }

        public static async Task ExecuteWithDelayAsync(
            this IRestClient client,
            RestRequest request,
            int executionDelayInMs = BaseDelayUntilEventSourceReachASyncStateMs,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);

            await ExecuteWithDelayInternalAsync(
                async () =>
                {
                    await client.ExecuteAsync(request, cancellationToken);
                    return true;
                },
                executionDelayInMs,
                executionDelayInMs,
                cancellationToken
            );
        }

        private static async Task<T> ExecuteWithDelayInternalAsync<T>(
            Func<Task<T>> operation,
            int delayBeforeMs,
            int delayAfterMs,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delayBeforeMs, cancellationToken);

            try
            {
                return await operation();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unexpected error during HTTP delayed execution: {ex.Message}", ex);
            }
            finally
            {
                await Task.Delay(delayAfterMs, cancellationToken);
            }
        }
    }
}
