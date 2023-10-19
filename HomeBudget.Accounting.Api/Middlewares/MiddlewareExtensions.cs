using Microsoft.AspNetCore.Builder;

namespace HomeBudget_Accounting_Api.Middlewares
{
    internal static class MiddlewareExtensions
    {
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CorrelationIdMiddleware>();
        }
    }
}
