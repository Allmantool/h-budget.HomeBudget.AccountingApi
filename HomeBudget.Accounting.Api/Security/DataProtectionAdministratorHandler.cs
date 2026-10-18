using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HomeBudget.Accounting.Api.Security
{
    internal sealed class DataProtectionAdministratorHandler(
        IOptionsMonitor<DataProtectionApiOptions> options)
        : AuthorizationHandler<DataProtectionAdministratorRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            DataProtectionAdministratorRequirement requirement)
        {
            var current = options.CurrentValue;
            if (!current.Enabled || !HasAdministratorClaim(context, current) || !HasScope(context, current.RequiredScope))
            {
                return Task.CompletedTask;
            }

            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        private static bool HasAdministratorClaim(
            AuthorizationHandlerContext context,
            DataProtectionApiOptions options)
        {
            return context.User.Claims.Any(claim =>
                string.Equals(claim.Type, options.AdministratorClaimType, StringComparison.Ordinal) &&
                string.Equals(claim.Value, options.AdministratorClaimValue, StringComparison.Ordinal));
        }

        private static bool HasScope(AuthorizationHandlerContext context, string requiredScope)
        {
            return context.User.FindAll("scope")
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Any(scope => string.Equals(scope, requiredScope, StringComparison.Ordinal));
        }
    }
}
