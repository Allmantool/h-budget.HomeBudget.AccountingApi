using System.Threading;
using System.Threading.Tasks;

using RestSharp;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class HttpClientExtensions
    {
        private const int BaseDelayUntilEventSourceReachASyncStateMs = 10000;

        public static async Task<RestResponse<T>> ExecuteWithDelayAsync<T>(
            this IRestClient client,
            RestRequest request,
            int executionDelayInMs = BaseDelayUntilEventSourceReachASyncStateMs,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(executionDelayInMs, cancellationToken);

            var response = await client.ExecuteAsync<T>(request, cancellationToken);

            await Task.Delay(executionDelayInMs, cancellationToken);

            return response;
        }

        public static async Task ExecuteWithDelayAsync(
            this IRestClient client,
            RestRequest request,
            int executionDelayInMs = BaseDelayUntilEventSourceReachASyncStateMs,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(executionDelayInMs, cancellationToken);

            await client.ExecuteAsync(request, cancellationToken);

            await Task.Delay(executionDelayInMs, cancellationToken);
        }
    }
}
