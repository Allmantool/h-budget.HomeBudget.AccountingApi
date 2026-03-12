using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using HomeBudget.Accounting.Notifications.Endpoints;
using HomeBudget.Accounting.Notifications.Services;

namespace HomeBudget.Accounting.Notifications.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection AddNotifications(this IServiceCollection services)
        {
            services.AddSingleton<INotificationChannel, NotificationChannel>();

            return services;
        }

        public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder app)
        {
            app.MapOperationNotifications();

            return app;
        }
    }
}
