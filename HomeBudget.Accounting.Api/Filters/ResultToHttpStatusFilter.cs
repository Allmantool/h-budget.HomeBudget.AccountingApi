using System.Reflection;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HomeBudget.Accounting.Api.Filters
{
    internal sealed class ResultToHttpStatusFilter : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(
            ResultExecutingContext context,
            ResultExecutionDelegate next)
        {
            if (context.Result is ObjectResult objectResult
                && TryGetFailureMessage(objectResult.Value, out var statusMessage))
            {
                objectResult.StatusCode = ApiErrorStatusCodeClassifier.FromFailureMessage(statusMessage);
            }

            await next();
        }

        private static bool TryGetFailureMessage(object value, out string statusMessage)
        {
            statusMessage = null;

            if (value == null)
            {
                return false;
            }

            var resultType = value.GetType();
            var isSucceededProperty = resultType.GetProperty("IsSucceeded", BindingFlags.Public | BindingFlags.Instance);
            var statusMessageProperty = resultType.GetProperty("StatusMessage", BindingFlags.Public | BindingFlags.Instance);

            if (isSucceededProperty?.PropertyType != typeof(bool) || statusMessageProperty?.PropertyType != typeof(string))
            {
                return false;
            }

            var isSucceeded = (bool)isSucceededProperty.GetValue(value);
            if (isSucceeded)
            {
                return false;
            }

            statusMessage = (string)statusMessageProperty.GetValue(value);
            return true;
        }
    }
}
