using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Polly;
using Polly.Retry;
using RestSharp;

namespace HomeBudget.Accounting.Api.IntegrationTests.Policies
{
    internal static class RestSharpRetryPolicies
    {
        private const int DefaultRetryAttempts = 3;
        private const int DefaultRetryRise = 1;

        public static AsyncRetryPolicy<RestResponse<T>> BuildRetryPolicy<T>(
            int retryAttempts = DefaultRetryAttempts,
            int retryRise = DefaultRetryRise)
        {
            return BuildPolicyInternal<RestResponse<T>>(
                r => r.StatusCode == 0 || ((int)r.StatusCode >= 500 && (int)r.StatusCode < 600),
                retryAttempts,
                retryRise);
        }

        public static AsyncRetryPolicy<RestResponse> BuildRetryPolicy(
            int retryAttempts = DefaultRetryAttempts,
            int retryRise = DefaultRetryRise)
        {
            return BuildPolicyInternal<RestResponse>(
                r => r.StatusCode == 0 || ((int)r.StatusCode >= 500 && (int)r.StatusCode < 600),
                retryAttempts,
                retryRise);
        }

        public static Task<RestResponse<T>> ExecuteWithRetryAsync<T>(
            this IRestClient client,
            RestRequest request,
            int retryAttempts = DefaultRetryAttempts,
            int retryRise = DefaultRetryRise,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);

            var policy = BuildRetryPolicy<T>(retryAttempts, retryRise);
            var context = CreateContext(client, request);

            return policy.ExecuteAsync(_ => client.ExecuteAsync<T>(request, cancellationToken), context);
        }

        public static Task<RestResponse> ExecuteWithRetryAsync(
            this IRestClient client,
            RestRequest request,
            int retryAttempts = DefaultRetryAttempts,
            int retryRise = DefaultRetryRise,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);

            var policy = BuildRetryPolicy(retryAttempts, retryRise);
            var context = CreateContext(client, request);

            return policy.ExecuteAsync(_ => client.ExecuteAsync(request, cancellationToken), context);
        }

        private static AsyncRetryPolicy<TResponse> BuildPolicyInternal<TResponse>(
            Func<TResponse, bool> shouldRetry,
            int retryAttempts,
            int retryRise)
        {
            retryAttempts = Math.Max(0, retryAttempts);
            retryRise = Math.Max(1, retryRise);

            return Policy<TResponse>
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .OrResult(shouldRetry)
                .WaitAndRetryAsync(
                    retryCount: retryAttempts,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(retryRise, attempt)),
                    onRetry: (outcome, delay, attempt, context) =>
                    {
                        var url = context.TryGetValue("Url", out var u) ? u?.ToString() : "<unknown>";
                        var method = context.TryGetValue("Method", out var m) ? m?.ToString() : "<unknown>";
                        var status = outcome.Result?.GetType()
                            .GetProperty("StatusCode")?.GetValue(outcome.Result)?.ToString() ?? "n/a";

                        System.Diagnostics.Debug.WriteLine(
                            $"Retry {attempt} after {delay.TotalSeconds:F1}s for {method} {url} (status={status})");
                    });
        }

        private static Context CreateContext(IRestClient client, RestRequest request) =>
            new()
            {
                ["Url"] = client.BuildUri(request)?.ToString(),
                ["Method"] = request.Method.ToString()
            };
    }
}
