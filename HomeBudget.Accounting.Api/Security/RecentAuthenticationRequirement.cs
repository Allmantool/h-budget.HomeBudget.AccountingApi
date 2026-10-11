using Microsoft.AspNetCore.Authorization;

namespace HomeBudget.Accounting.Api.Security
{
    internal sealed class RecentAuthenticationRequirement : IAuthorizationRequirement
    {
    }
}
