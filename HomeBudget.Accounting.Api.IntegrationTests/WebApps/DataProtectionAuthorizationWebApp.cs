using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Api.IntegrationTests.DataProtection;
using HomeBudget.Accounting.Api.Security;
using HomeBudget.Accounting.Infrastructure.DataProtection;

using AccountingWorker = HomeBudget.Accounting.Workers.OperationsConsumer;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal sealed class DataProtectionAuthorizationWebApp
        : BaseTestWebApp<Program, AccountingWorker.Program>
    {
        public DataProtectionAuthorizationWebApp(
            bool registerTestExecutor = true,
            bool registerRuntimeReadiness = true)
        {
            ShouldInitializeWorkers = false;
            ConfigureTestServices = services =>
            {
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = DataProtectionTestAuthenticationHandler.TestSchemeName;
                        options.DefaultChallengeScheme = DataProtectionTestAuthenticationHandler.TestSchemeName;
                        options.DefaultForbidScheme = DataProtectionTestAuthenticationHandler.TestSchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, DataProtectionTestAuthenticationHandler>(
                        DataProtectionTestAuthenticationHandler.TestSchemeName,
                        _ => { });
                services.AddAuthorization();
                services.PostConfigure<DataProtectionApiOptions>(options =>
                {
                    options.Enabled = true;
                    options.Authority = "https://integration-issuer.invalid";
                    options.Audience = "home-ledger-accounting-integration";
                    options.AdministratorClaimType = ClaimTypes.Role;
                    options.AdministratorClaimValue = "data-protection-administrator";
                    options.RequiredScope = DataProtectionPolicies.AdministrativeScope;
                    options.RecentAuthenticationMinutes = 15;
                    options.ConfirmationLifetimeMinutes = 5;
                    options.ClockSkewSeconds = 60;
                });
                if (registerTestExecutor)
                {
                    services.Replace(ServiceDescriptor.Scoped<
                        IDataProtectionOperationExecutor,
                        ControllableDataProtectionOperationExecutor>());
                }

                if (registerRuntimeReadiness)
                {
                    services.PostConfigure<DataProtectionRecoveryOptions>(options =>
                    {
                        options.Enabled = true;
                        options.BackupProvider = "validated-integration-backup-provider";
                        options.IndependentStore = "validated-integration-independent-store";
                        options.KeyReference = "validated-integration-key-reference";
                        options.NotificationTarget = "validated-integration-notification-target";
                        options.RecoveryHostResourceId = "validated-integration-recovery-host";
                    });
                    services.Replace(ServiceDescriptor.Singleton<
                        IDataProtectionRuntimeReadinessProbe,
                        ValidatedDataProtectionRuntimeReadinessProbe>());
                }
            };
        }
    }
}
