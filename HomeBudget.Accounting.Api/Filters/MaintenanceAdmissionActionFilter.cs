using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.DataProtection.Capture;

namespace HomeBudget.Accounting.Api.Filters
{
    internal sealed class MaintenanceAdmissionActionFilter(
        IMaintenanceAdmissionFence admissionFence,
        IActiveLedgerGenerationResolver generationResolver) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            if (IsReadRequest(context.HttpContext.Request.Method)
                || context.ActionDescriptor.EndpointMetadata.OfType<AllowDuringMaintenanceAttribute>().Any())
            {
                await next().ConfigureAwait(false);
                return;
            }

            var generation = await generationResolver
                .GetActiveAsync(context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            await using var permit = await admissionFence.AcquireAdmissionPermitAsync(
                MaintenanceScopeKey.ForLedger(generation),
                generation.LedgerGenerationId,
                context.HttpContext.RequestAborted).ConfigureAwait(false);
            await next().ConfigureAwait(false);
        }

        private static bool IsReadRequest(string method)
        {
            return HttpMethods.IsGet(method)
                || HttpMethods.IsHead(method)
                || HttpMethods.IsOptions(method);
        }
    }
}
