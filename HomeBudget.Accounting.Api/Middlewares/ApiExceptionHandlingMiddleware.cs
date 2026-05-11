using System;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Api.Filters;
using HomeBudget.Core.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Accounting.Api.Middlewares
{
    internal sealed class ApiExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionHandlingMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (RequestValidationException ex)
            {
                await WriteFailureAsync(context, StatusCodes.Status400BadRequest, ex.Message);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled API exception.");

                var statusCode = ApiErrorStatusCodeClassifier.FromException(ex);
                var message = ApiErrorStatusCodeClassifier.MessageForException(ex);

                await WriteFailureAsync(context, statusCode, message);
            }
        }

        private static async Task WriteFailureAsync(
            HttpContext context,
            int statusCode,
            string message)
        {
            if (context.Response.HasStarted)
            {
                throw new InvalidOperationException("The response has already started.");
            }

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var body = JsonSerializer.Serialize(
                Result<object>.Failure(message),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            await context.Response.WriteAsync(body);
        }
    }
}
