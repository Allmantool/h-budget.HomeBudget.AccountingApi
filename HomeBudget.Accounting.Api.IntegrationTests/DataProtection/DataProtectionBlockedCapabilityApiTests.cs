using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using FluentAssertions;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.WebApps;
using HomeBudget.Accounting.Api.Security;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    [TestFixture]
    [Category(TestTypes.Integration)]
    [NonParallelizable]
    internal sealed class DataProtectionBlockedCapabilityApiTests : BaseIntegrationTests
    {
        private readonly DataProtectionAuthorizationWebApp _webApp = new(
            registerTestExecutor: false,
            registerRuntimeReadiness: false);
        private RestClient _client;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            await _webApp.InitAsync(requireIntegrationCategory: true);
            _client = _webApp.RestHttpClientAllowingHttpErrors;
        }

        [Test]
        public async Task Capability_WhenNoExecutorsExist_ReportsBlockedAndRejectsPlanWithoutMutation()
        {
            var before = await DataProtectionOperationSnapshotReader.ReadAsync(
                TestContainers,
                TestContext.CurrentContext.CancellationToken);

            var capability = await _client.ExecuteAsync(Authenticate(
                new RestRequest("/data-protection/v1/capabilities", Method.Get)));

            capability.StatusCode.Should().Be(HttpStatusCode.OK, Describe(capability));
            using var document = JsonDocument.Parse(capability.Content);
            var payload = GetPayload(document.RootElement);
            payload.GetProperty("status").GetString().Should().Be("blocked");
            payload.GetProperty("executableOperationKinds").GetArrayLength().Should().Be(0);
            payload.GetProperty("missingPrerequisites").EnumerateArray()
                .Select(item => item.GetString())
                .Should().Contain([
                    "data-protection-disabled",
                    "backup-provider-not-configured",
                    "independent-store-not-configured",
                    "key-reference-not-configured",
                    "notification-target-not-configured",
                    "recovery-host-not-configured",
                    "operation-executors-not-configured"
                ]);

            var plan = await _client.ExecuteAsync(Authenticate(
                new RestRequest("/data-protection/v1/plans", Method.Post)
                    .AddJsonBody(new
                    {
                        operationKind = "ledger-reset",
                        environmentId = "integration",
                        ledgerId = $"ledger-{Guid.NewGuid():N}",
                        ledgerGenerationId = "legacy"
                    })));

            plan.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, Describe(plan));
            var after = await DataProtectionOperationSnapshotReader.ReadAsync(
                TestContainers,
                TestContext.CurrentContext.CancellationToken);
            after.Should().Equal(before);
        }

        private static RestRequest Authenticate(RestRequest request)
        {
            return request
                .AddHeader(DataProtectionTestAuthenticationHandler.SubjectHeader, "integration-administrator")
                .AddHeader(DataProtectionTestAuthenticationHandler.RoleHeader, "data-protection-administrator")
                .AddHeader(
                    DataProtectionTestAuthenticationHandler.ScopeHeader,
                    DataProtectionPolicies.AdministrativeScope)
                .AddHeader(
                    DataProtectionTestAuthenticationHandler.AuthTimeHeader,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        private static JsonElement GetPayload(JsonElement root)
        {
            return root.TryGetProperty("payload", out var wrappedPayload) ? wrappedPayload : root;
        }

        private static string Describe(RestResponse response)
        {
            return response is null
                ? "Response was null."
                : $"HTTP {(int)response.StatusCode} {response.StatusCode}; " +
                  $"content='{response.Content}'; error='{response.ErrorMessage}'";
        }
    }
}
