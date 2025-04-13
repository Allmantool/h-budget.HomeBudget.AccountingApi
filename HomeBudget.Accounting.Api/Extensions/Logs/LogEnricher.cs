using System;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Serilog;

using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Api.Extensions.Logs
{
    internal static class LogEnricher
    {
        internal static void HttpRequestEnricher(IDiagnosticContext diagnosticContext, HttpContext httpContext)
        {
            var requestHeaders = httpContext.Request.Headers;
            var responseHeaders = httpContext.Response.Headers;

            _ = requestHeaders.TryGetValue(HttpHeaderKeys.CorrelationId, out var correlationIdFromRequest);
            _ = responseHeaders.TryGetValue(HttpHeaderKeys.CorrelationId, out var correlationIdFromResponse);

            var httpContextInfo = new HttpContextInfo
            {
                Protocol = httpContext.Request.Protocol,
                Scheme = httpContext.Request.Scheme,
                IpAddress = httpContext.Connection.RemoteIpAddress.ToString(),
                Host = httpContext.Request.Host.ToString(),
                User = GetUserInfo(httpContext.User),
                CorrelationIdFromRequest = correlationIdFromRequest,
                CorrelationIdFromResponse = correlationIdFromResponse
            };

            diagnosticContext.Set("HttpContext", httpContextInfo, true);
        }

        private static string GetUserInfo(ClaimsPrincipal user)
        {
            if (user.Identity != null && user.Identity.IsAuthenticated)
            {
                return user.Identity.Name;
            }

            return Environment.UserName;
        }
    }
}
