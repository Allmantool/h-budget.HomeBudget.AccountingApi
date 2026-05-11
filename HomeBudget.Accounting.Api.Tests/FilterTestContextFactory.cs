using System.Collections.Generic;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace HomeBudget.Accounting.Api.Tests
{
    internal static class FilterTestContextFactory
    {
        public static ResultExecutingContext CreateResultExecutingContext(IActionResult result)
        {
            return new ResultExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                [],
                result,
                new object());
        }

        public static ResultExecutedContext CreateResultExecutedContext(ResultExecutingContext context)
        {
            return new ResultExecutedContext(
                context,
                context.Filters,
                context.Result,
                context.Controller);
        }
    }
}
