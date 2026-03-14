using HomeBudget.Accounting.Notifications.Endpoints;
using HomeBudget.Accounting.Notifications.Hubs;
using HomeBudget.Accounting.Notifications.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Accounting.Notifications.Configuration
{
    public static class DependencyRegistrations
    {
        public static IServiceCollection AddNotifications(this IServiceCollection services)
        {
            services.AddSignalR();
            services.AddSingleton<INotificationChannel, NotificationChannel>();

            return services;
        }

        public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder app)
        {
            app.MapOperationNotifications();
            app.MapHub<LedgerNotificationsHub>(LedgerNotificationsHub.Route);

            return app;
        }
    }
}
