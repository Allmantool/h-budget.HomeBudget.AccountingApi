using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Filters;

namespace HomeBudget.Accounting.Api.IntegrationTests.Filters
{
    internal sealed class DiscardResponseAfterAcceptedPaymentCommandFilter : IAsyncActionFilter
    {
        internal const string HeaderName = "X-Test-Discard-Response-After-Acceptance";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var execution = await next();

            if (execution.Exception is not null || execution.Canceled ||
                !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var value) ||
                value != "true")
            {
                return;
            }

            context.HttpContext.Abort();
        }
    }
}
