using Microsoft.AspNetCore.Builder;

namespace HomeBudget.Accounting.Api.Middlewares
{
    internal static class MiddlewareExtensions
    {
        public static IApplicationBuilder UseCustomCorrelationId(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CorrelationIdMiddleware>();
        }
    }
}
