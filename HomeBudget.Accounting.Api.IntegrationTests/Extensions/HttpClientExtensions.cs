using System.Threading;
using System.Threading.Tasks;

using RestSharp;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class HttpClientExtensions
    {
        private const int DelayUntilEventSourceReachASyncStateMs = 1000;

        public static async Task<RestResponse<T>> ExecuteWithDelayAsync<T>(
            this IRestClient client,
            RestRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await client.ExecuteAsync<T>(request, cancellationToken);

            await Task.Delay(DelayUntilEventSourceReachASyncStateMs, cancellationToken);

            return response;
        }

        public static async Task ExecuteWithDelayAsync(
            this IRestClient client,
            RestRequest request,
            CancellationToken cancellationToken = default)
        {
            await client.ExecuteAsync(request, cancellationToken);

            await Task.Delay(DelayUntilEventSourceReachASyncStateMs, cancellationToken);
        }
    }
}
