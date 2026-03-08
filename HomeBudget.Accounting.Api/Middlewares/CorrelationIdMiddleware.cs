using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Api.Middlewares
{
    internal class CorrelationIdMiddleware(
        ILogger<CorrelationIdMiddleware> logger,
        RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            var requestPath = context.Request.Path;

            var isHealthCheckRequest =
                string.Equals(requestPath, "/health", StringComparison.OrdinalIgnoreCase);

            if (isHealthCheckRequest)
            {
                await next(context);

                return;
            }

            var requestHeaders = context.Request.Headers;

            var traceId = Activity.Current?.TraceId.ToString();

            var correlationHeaderExists =
                requestHeaders.TryGetValue(HttpHeaderKeys.CorrelationId, out var header);

            var correlationId =
                correlationHeaderExists
                    ? header.ToString()
                    : traceId ?? Guid.NewGuid().ToString();

            if (!correlationHeaderExists && !isHealthCheckRequest)
            {
                logger.LogWarning(
                    "CorrelationId missing. Generated new one: {CorrelationId}",
                    correlationId);
            }

            context.Response.Headers[HttpHeaderKeys.CorrelationId] = correlationId;

            if (traceId != null)
            {
                context.Response.Headers[HttpHeaderKeys.TraceId] = traceId;
            }

            using (LogContext.PushProperty(HttpHeaderKeys.CorrelationId, correlationId))
            using (LogContext.PushProperty(HttpHeaderKeys.TraceId, traceId))
            {
                await next(context);
            }
        }
    }
}
