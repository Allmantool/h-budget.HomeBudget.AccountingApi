using System.Security.Claims;

namespace HomeBudget.Accounting.Api.Security
{
    internal static class DataProtectionActor
    {
        public static string GetId(ClaimsPrincipal principal)
        {
            return principal?.FindFirst("sub")?.Value ??
                   principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
