using System;
using System.Collections.Generic;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using NUnit.Framework;

using HomeBudget.Accounting.Api.Security;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    internal sealed class DataProtectionSecurityConfigurationTests
    {
        [Test]
        public void EnabledApi_WhenIssuerAudienceAndAdminPolicyAreMissing_FailsClosed()
        {
            var services = CreateServices(new Dictionary<string, string>
            {
                ["DataProtectionApi:Enabled"] = "true"
            });
            using var provider = services.BuildServiceProvider();

            Action resolveOptions = () => _ = provider
                .GetRequiredService<IOptions<DataProtectionApiOptions>>()
                .Value;

            resolveOptions.Should().Throw<OptionsValidationException>();
        }

        [Test]
        public void DisabledApi_WhenIdentityConfigurationIsMissing_RemainsDisabled()
        {
            var services = CreateServices(new Dictionary<string, string>());
            using var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<IOptions<DataProtectionApiOptions>>().Value;

            options.Enabled.Should().BeFalse();
        }

        [Test]
        public void EnabledApi_WhenConfigurationIsComplete_UsesCanonicalAdministrativeScope()
        {
            var services = CreateServices(new Dictionary<string, string>
            {
                ["DataProtectionApi:Enabled"] = "true",
                ["DataProtectionApi:Authority"] = "https://identity.example.invalid",
                ["DataProtectionApi:Audience"] = "home-ledger-accounting",
                ["DataProtectionApi:AdministratorClaimType"] = "role",
                ["DataProtectionApi:AdministratorClaimValue"] = "data-protection-administrator",
                ["DataProtectionApi:RequiredScope"] = "home-ledger.data-protection.admin"
            });
            using var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<IOptions<DataProtectionApiOptions>>().Value;

            options.Enabled.Should().BeTrue();
            options.RequiredScope.Should().Be("home-ledger.data-protection.admin");
        }

        [Test]
        public void EnabledApi_WhenAdministrativeScopeDiffersFromGatewayContract_FailsClosed()
        {
            var services = CreateServices(new Dictionary<string, string>
            {
                ["DataProtectionApi:Enabled"] = "true",
                ["DataProtectionApi:Authority"] = "https://identity.example.invalid",
                ["DataProtectionApi:Audience"] = "home-ledger-accounting",
                ["DataProtectionApi:AdministratorClaimType"] = "role",
                ["DataProtectionApi:AdministratorClaimValue"] = "data-protection-administrator",
                ["DataProtectionApi:RequiredScope"] = "home-ledger.data-protection"
            });
            using var provider = services.BuildServiceProvider();

            Action resolveOptions = () => _ = provider
                .GetRequiredService<IOptions<DataProtectionApiOptions>>()
                .Value;

            resolveOptions.Should().Throw<OptionsValidationException>();
        }

        private static ServiceCollection CreateServices(IReadOnlyDictionary<string, string> values)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtectionSecurity(configuration, new TestWebHostEnvironment());
            return services;
        }

        private sealed class TestWebHostEnvironment : IWebHostEnvironment
        {
            public string ApplicationName { get; set; } = "HomeBudget.Accounting.Api.IntegrationTests";
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string WebRootPath { get; set; } = AppContext.BaseDirectory;
            public string EnvironmentName { get; set; } = "Production";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
