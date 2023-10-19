using Microsoft.AspNetCore.Builder;

namespace HomeBudget.Accounting.Api.Middlewares
{
    internal static class MiddlewareExtensions
    {
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CorrelationIdMiddleware>();
        }
    }
}
