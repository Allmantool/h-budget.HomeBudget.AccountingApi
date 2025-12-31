using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Api.Middlewares
{
    internal sealed class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = context.Request.Headers[HttpHeaderKeys.CorrelationId]
                .FirstOrDefault()
                ?? Guid.NewGuid().ToString("N");

            context.Items[HttpHeaderKeys.CorrelationId] = correlationId;

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HttpHeaderKeys.CorrelationId] = correlationId;
                return Task.CompletedTask;
            });

            using (Serilog.Context.LogContext.PushProperty(HttpHeaderKeys.CorrelationId, correlationId))
            {
                await _next(context);
            }
        }
    }
}
