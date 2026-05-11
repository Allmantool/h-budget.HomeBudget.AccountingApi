using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using RestSharp;

using HomeBudget.Accounting.Api.IntegrationTests.Policies;

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
                async () => await client.ExecuteWithRetryAsync<T>(request, cancellationToken: cancellationToken),
                executionDelayBeforeInMs,
                executionDelayAfterInMs,
                cancellationToken
            );
        }

        public static async Task<RestResponse<T>> ExecuteWithDelayAsync<T>(
            this IRestClient client,
            RestRequest request,
            HttpStatusCode[] allowedStatusCodes,
            int executionDelayBeforeInMs = BaseDelayUntilEventSourceReachASyncStateMs,
            int executionDelayAfterInMs = BaseDelayUntilEventSourceReachASyncStateMs,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allowedStatusCodes);

            return await ExecuteWithDelayInternalAsync(
                async () => await client.ExecuteAllowingHttpErrorAsync<T>(
                    request,
                    allowedStatusCodes,
                    cancellationToken),
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
                    await client.ExecuteWithRetryAsync(request, cancellationToken: cancellationToken);

                    return true;
                },
                executionDelayInMs,
                executionDelayInMs,
                cancellationToken
            );
        }

        public static async Task<RestResponse<T>> ExecuteAllowingHttpErrorAsync<T>(
            this IRestClient client,
            RestRequest request,
            HttpStatusCode[] allowedStatusCodes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allowedStatusCodes);

            RestResponse<T> response;

            try
            {
                response = await client.ExecuteWithRetryAsync<T>(request, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Unexpected transport error during HTTP execution for {DescribeRequest(client, request)}: {ex.Message}",
                    ex);
            }

            EnsureHttpResponseStatusIsExpected(client, request, response, allowedStatusCodes);

            return response;
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

        private static void EnsureHttpResponseStatusIsExpected<T>(
            IRestClient client,
            RestRequest request,
            RestResponse<T> response,
            HttpStatusCode[] allowedStatusCodes)
        {
            if (response == null)
            {
                throw new InvalidOperationException($"No HTTP response was received for {DescribeRequest(client, request)}.");
            }

            if (response.StatusCode == 0)
            {
                throw new InvalidOperationException(
                    $"No HTTP status was received for {DescribeRequest(client, request)}. " +
                    $"ResponseStatus={response.ResponseStatus}, ErrorMessage='{response.ErrorMessage}', " +
                    $"Exception='{response.ErrorException?.GetType().FullName}: {response.ErrorException?.Message}', Content='{response.Content}'");
            }

            if (response.IsSuccessful || allowedStatusCodes.Contains(response.StatusCode))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Unexpected HTTP status for {DescribeRequest(client, request)}. " +
                $"Expected one of [{string.Join(", ", allowedStatusCodes.Select(s => $"{(int)s} {s}"))}] or success, " +
                $"but received {(int)response.StatusCode} {response.StatusCode}. " +
                $"ResponseStatus={response.ResponseStatus}, ErrorMessage='{response.ErrorMessage}', Content='{response.Content}'");
        }

        private static string DescribeRequest(IRestClient client, RestRequest request)
        {
            var resource = string.IsNullOrWhiteSpace(request.Resource) ? "<empty-resource>" : request.Resource;
            string uri;

            try
            {
                uri = client.BuildUri(request)?.ToString() ?? resource;
            }
            catch
            {
                uri = resource;
            }

            return $"{request.Method} {uri}";
        }
    }
}
