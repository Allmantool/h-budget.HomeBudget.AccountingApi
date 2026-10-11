using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal sealed class DataProtectionTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string TestSchemeName = "DataProtectionTest";
        internal const string SubjectHeader = "X-Test-Subject";
        internal const string RoleHeader = "X-Test-Role";
        internal const string ScopeHeader = "X-Test-Scope";
        internal const string AuthTimeHeader = "X-Test-Auth-Time";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubjectHeader, out var subjects) ||
                string.IsNullOrWhiteSpace(subjects.ToString()))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, subjects.ToString())
            };

            AddClaimIfPresent(claims, RoleHeader, ClaimTypes.Role);
            AddClaimIfPresent(claims, ScopeHeader, "scope");
            AddClaimIfPresent(claims, AuthTimeHeader, "auth_time");

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, TestSchemeName));
            var ticket = new AuthenticationTicket(principal, TestSchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        private void AddClaimIfPresent(List<Claim> claims, string headerName, string claimType)
        {
            if (Request.Headers.TryGetValue(headerName, out var values) &&
                !string.IsNullOrWhiteSpace(values.ToString()))
            {
                claims.Add(new Claim(claimType, values.ToString()));
            }
        }
    }
}
