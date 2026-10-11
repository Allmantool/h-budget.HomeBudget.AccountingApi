using System;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Infrastructure.Extensions;

namespace HomeBudget.Accounting.Api.Security
{
    internal static class DataProtectionSecurityExtensions
    {
        public static IServiceCollection AddDataProtectionSecurity(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(environment);

            var section = configuration.GetSection(DataProtectionApiOptions.SectionName);
            var configured = section.Get<DataProtectionApiOptions>() ?? new DataProtectionApiOptions();
            services
                .AddOptions<DataProtectionApiOptions>()
                .Bind(section)
                .Validate(IsValid, BuildValidationMessage())
                .ValidateOnStart();
            services
                .AddOptions<DataProtectionRecoveryOptions>()
                .Bind(configuration.GetSection(DataProtectionRecoveryOptions.SectionName))
                .Validate(DataProtectionRecoveryOptions.IsValid, "Data-protection recovery targets or prerequisites are invalid.")
                .ValidateOnStart();

            if (environment.IsIntegrationTesting())
            {
                services.AddAuthentication();
            }
            else
            {
                services
                    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options => ConfigureJwtBearer(options, configured));
            }

            services.AddAuthorization(options =>
            {
                options.AddPolicy(DataProtectionPolicies.Administrator, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(new DataProtectionAdministratorRequirement());
                });
                options.AddPolicy(DataProtectionPolicies.Destructive, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(
                        new DataProtectionAdministratorRequirement(),
                        new RecentAuthenticationRequirement());
                });
            });
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IAuthorizationHandler, DataProtectionAdministratorHandler>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IAuthorizationHandler, RecentAuthenticationHandler>());
            services.TryAddSingleton<
                IDataProtectionRuntimeReadinessProbe,
                UnavailableDataProtectionRuntimeReadinessProbe>();

            return services;
        }

        internal static bool IsValid(DataProtectionApiOptions options)
        {
            if (!options.Enabled)
            {
                return true;
            }

            return IsHttpsAuthority(options.Authority) &&
                   !string.IsNullOrWhiteSpace(options.Audience) &&
                   !string.IsNullOrWhiteSpace(options.AdministratorClaimType) &&
                   !string.IsNullOrWhiteSpace(options.AdministratorClaimValue) &&
                   string.Equals(
                       options.RequiredScope,
                       DataProtectionPolicies.AdministrativeScope,
                       StringComparison.Ordinal) &&
                   options.RecentAuthenticationMinutes is >= 1 and <= 60 &&
                   options.ConfirmationLifetimeMinutes is >= 1 and <= 30 &&
                   options.ClockSkewSeconds is >= 0 and <= 300;
        }

        private static bool IsHttpsAuthority(string authority)
        {
            return Uri.TryCreate(authority, UriKind.Absolute, out var uri) &&
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildValidationMessage()
        {
            return "Enabled data-protection API requires HTTPS authority, audience, administrator claim type/value, " +
                   "required scope, recent-authentication and confirmation lifetimes, and bounded clock skew.";
        }

        private static void ConfigureJwtBearer(
            JwtBearerOptions jwt,
            DataProtectionApiOptions configured)
        {
            jwt.MapInboundClaims = false;
            jwt.RequireHttpsMetadata = configured.RequireHttpsMetadata;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                NameClaimType = "sub",
                RoleClaimType = configured.AdministratorClaimType ?? "role"
            };

            if (!configured.Enabled)
            {
                jwt.TokenValidationParameters.ValidIssuer = "urn:home-ledger:data-protection:disabled";
                jwt.TokenValidationParameters.ValidAudience = "urn:home-ledger:data-protection:disabled";
                return;
            }

            jwt.Authority = configured.Authority;
            jwt.Audience = configured.Audience;
        }
    }
}
