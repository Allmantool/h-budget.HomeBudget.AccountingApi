using System;
using System.Globalization;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HomeBudget.Accounting.Api.Security
{
    internal sealed class RecentAuthenticationHandler(
        IOptionsMonitor<DataProtectionApiOptions> options,
        TimeProvider timeProvider) : AuthorizationHandler<RecentAuthenticationRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RecentAuthenticationRequirement requirement)
        {
            var authTimeValue = context.User.FindFirst("auth_time")?.Value;
            if (!long.TryParse(authTimeValue, NumberStyles.None, CultureInfo.InvariantCulture, out var authTimeSeconds))
            {
                return Task.CompletedTask;
            }

            DateTimeOffset authTime;
            try
            {
                authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Task.CompletedTask;
            }

            var current = options.CurrentValue;
            var now = timeProvider.GetUtcNow();
            var oldestAllowed = now.AddMinutes(-current.RecentAuthenticationMinutes);
            var latestAllowed = now.AddSeconds(current.ClockSkewSeconds);
            if (authTime >= oldestAllowed && authTime <= latestAllowed)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
