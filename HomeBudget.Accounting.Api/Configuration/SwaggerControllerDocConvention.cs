using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace HomeBudget.Accounting.Api.Configuration
{
    internal class SwaggerControllerDocConvention : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            foreach (var attribute in controller.Attributes)
            {
                if (attribute.GetType() != typeof(RouteAttribute))
                {
                    continue;
                }

                var routeAttribute = (RouteAttribute)attribute;

                if (!string.IsNullOrWhiteSpace(routeAttribute.Name))
                {
                    controller.ControllerName = routeAttribute.Name;
                }
            }
        }
    }
}
