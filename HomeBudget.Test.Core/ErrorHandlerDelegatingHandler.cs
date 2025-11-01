using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Test.Core
{
    public class ErrorHandlerDelegatingHandler : DelegatingHandler
    {
        public ErrorHandlerDelegatingHandler(HttpMessageHandler innerHandler)
            : base(innerHandler) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception("Network error while calling API", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"API Error: {(int)response.StatusCode} {response.ReasonPhrase}. Content: {content}");
            }

            return response;
        }
    }
}
