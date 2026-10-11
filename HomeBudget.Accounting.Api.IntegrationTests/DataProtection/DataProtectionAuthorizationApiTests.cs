using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading;
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
    internal sealed class DataProtectionAuthorizationApiTests : BaseIntegrationTests
    {
        private const string PlansEndpoint = "/data-protection/v1/plans";
        private const string OperationsEndpoint = "/data-protection/v1/operations";
        private const string CapabilitiesEndpoint = "/data-protection/v1/capabilities";
        private const string AdministratorRole = "data-protection-administrator";
        private const string AdministrativeScope = DataProtectionPolicies.AdministrativeScope;

        private readonly DataProtectionAuthorizationWebApp _webApp = new();
        private RestClient _client;

        [OneTimeSetUp]
        public override async Task SetupAsync()
        {
            await base.SetupAsync();
            await _webApp.InitAsync(requireIntegrationCategory: true);
            _client = _webApp.RestHttpClientAllowingHttpErrors;
        }

        [Test]
        public async Task Plan_WhenCalledDirectlyWithoutAuthentication_ReturnsUnauthorizedWithoutOperationMutation()
        {
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);

            var response = await _client.ExecuteAsync(CreatePlanRequest(CreateTarget()));

            await AssertOperationSnapshotUnchangedAsync(before);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, Describe(response));
        }

        [Test]
        public async Task Plan_WhenCallerLacksAdministratorRole_ReturnsForbiddenWithoutOperationMutation()
        {
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            var request = Authenticate(CreatePlanRequest(CreateTarget()), role: "ledger-user", FreshAuthTime());

            var response = await _client.ExecuteAsync(request);

            await AssertOperationSnapshotUnchangedAsync(before);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, Describe(response));
        }

        [Test]
        public async Task Plan_WhenCallerLacksAdministrativeScope_ReturnsForbiddenWithoutOperationMutation()
        {
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            var request = Authenticate(
                CreatePlanRequest(CreateTarget()),
                AdministratorRole,
                FreshAuthTime(),
                scope: "ledger.read");

            var response = await _client.ExecuteAsync(request);

            await AssertOperationSnapshotUnchangedAsync(before);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, Describe(response));
        }

        [Test]
        public async Task Start_WhenAuthenticationIsStale_ReturnsForbiddenWithoutOperationMutation()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            var staleAuthTime = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
            var request = Authenticate(CreateStartRequest(plan, target), AdministratorRole, staleAuthTime);

            var response = await _client.ExecuteAsync(request);

            await AssertOperationSnapshotUnchangedAsync(before);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, Describe(response));
        }

        [Test]
        public async Task Start_WhenConfirmationIsReused_RejectsSecondUseWithoutAnotherOperationEffect()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            var startRequest = CreateStartRequest(plan, target);

            var first = await _client.ExecuteAsync(Authenticate(startRequest, AdministratorRole, FreshAuthTime()));
            first.StatusCode.Should().Be(HttpStatusCode.Accepted, Describe(first));
            var afterFirst = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            afterFirst.Should().HaveCount(before.Count + 1, "the first confirmation use creates exactly one operation");

            var second = await _client.ExecuteAsync(Authenticate(
                CreateStartRequest(plan, target),
                AdministratorRole,
                FreshAuthTime()));

            await AssertOperationSnapshotUnchangedAsync(afterFirst);
            IsSafeClientRejection(second.StatusCode).Should().BeTrue(Describe(second));
        }

        [Test]
        public async Task Start_WhenConfirmationIsUsedConcurrently_CreatesExactlyOneOperation()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);

            var firstTask = _client.ExecuteAsync(Authenticate(
                CreateStartRequest(plan, target),
                AdministratorRole,
                FreshAuthTime()));
            var secondTask = _client.ExecuteAsync(Authenticate(
                CreateStartRequest(plan, target),
                AdministratorRole,
                FreshAuthTime()));
            var responses = await Task.WhenAll(firstTask, secondTask);

            responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.Accepted);
            responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.Conflict);
            var after = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            after.Should().HaveCount(before.Count + 1, "confirmation consumption and enqueue are atomic");
        }

        [Test]
        public async Task Start_WhenConfirmedActorChanges_RejectsWithoutOperationMutation()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);

            var response = await _client.ExecuteAsync(Authenticate(
                CreateStartRequest(plan, target),
                AdministratorRole,
                FreshAuthTime(),
                subject: "different-integration-administrator"));

            await AssertOperationSnapshotUnchangedAsync(before);
            IsSafeClientRejection(response.StatusCode).Should().BeTrue(Describe(response));
        }

        [Test]
        public async Task Start_WhenConfirmedOperationKindChanges_RejectsWithoutOperationMutation()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);

            var response = await _client.ExecuteAsync(Authenticate(
                CreateStartRequest(plan, target, operationKind: "projection-rebuild"),
                AdministratorRole,
                FreshAuthTime()));

            await AssertOperationSnapshotUnchangedAsync(before);
            IsSafeClientRejection(response.StatusCode).Should().BeTrue(Describe(response));
        }

        [Test]
        public async Task Start_WhenConfirmedTargetChanges_RejectsWithoutOperationMutation()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            var changedTarget = target.WithLedgerId($"other-{Guid.NewGuid():N}");

            var response = await _client.ExecuteAsync(Authenticate(
                CreateStartRequest(plan, changedTarget),
                AdministratorRole,
                FreshAuthTime()));

            await AssertOperationSnapshotUnchangedAsync(before);
            IsSafeClientRejection(response.StatusCode).Should().BeTrue(Describe(response));
        }

        [Test]
        public async Task Start_WhenConfirmedGenerationChanges_RejectsWithoutOperationMutation()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            var changedTarget = target.WithLedgerGenerationId($"generation-{Guid.NewGuid():N}");

            var response = await _client.ExecuteAsync(Authenticate(
                CreateStartRequest(plan, changedTarget),
                AdministratorRole,
                FreshAuthTime()));

            await AssertOperationSnapshotUnchangedAsync(before);
            IsSafeClientRejection(response.StatusCode).Should().BeTrue(Describe(response));
        }

        [Test]
        public async Task Start_WhenPlanFingerprintChanges_RejectsWithoutOperationMutation()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var before = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            var changedPlan = plan.WithPlanFingerprint(new string('f', 64));

            var response = await _client.ExecuteAsync(Authenticate(
                CreateStartRequest(changedPlan, target),
                AdministratorRole,
                FreshAuthTime()));

            await AssertOperationSnapshotUnchangedAsync(before);
            IsSafeClientRejection(response.StatusCode).Should().BeTrue(Describe(response));
        }

        [Test]
        public async Task Capabilities_WhenExecutorIsRegistered_ReportsOnlyExecutableContractKinds()
        {
            var response = await _client.ExecuteAsync(Authenticate(
                new RestRequest(CapabilitiesEndpoint, Method.Get),
                AdministratorRole,
                FreshAuthTime()));

            response.StatusCode.Should().Be(HttpStatusCode.OK, Describe(response));
            using var document = JsonDocument.Parse(response.Content);
            var payload = GetPayload(document.RootElement);
            payload.GetProperty("status").GetString().Should().Be("available");
            payload.GetProperty("recognizedOperationKinds").GetArrayLength().Should().Be(9);
            payload.GetProperty("executableOperationKinds").GetArrayLength().Should().Be(9);
            payload.GetProperty("missingPrerequisites").GetArrayLength().Should().Be(0);
        }

        [Test]
        public async Task Operation_AfterAcceptedStart_CanBePolledAndCancelled()
        {
            var target = CreateTarget();
            var plan = await CreateAuthorizedPlanAsync(target);
            var start = await _client.ExecuteAsync(Authenticate(
                CreateStartRequest(plan, target),
                AdministratorRole,
                FreshAuthTime()));
            start.StatusCode.Should().Be(HttpStatusCode.Accepted, Describe(start));
            using var startDocument = JsonDocument.Parse(start.Content);
            var operationId = GetPayload(startDocument.RootElement).GetProperty("operationId").GetGuid();

            var status = await _client.ExecuteAsync(Authenticate(
                new RestRequest($"{OperationsEndpoint}/{operationId:D}", Method.Get),
                AdministratorRole,
                FreshAuthTime()));
            status.StatusCode.Should().Be(HttpStatusCode.OK, Describe(status));
            using var statusDocument = JsonDocument.Parse(status.Content);
            GetPayload(statusDocument.RootElement).GetProperty("state").GetString().Should().Be("queued");

            var cancel = await _client.ExecuteAsync(Authenticate(
                new RestRequest($"{OperationsEndpoint}/{operationId:D}/cancel", Method.Post),
                AdministratorRole,
                FreshAuthTime()));
            cancel.StatusCode.Should().Be(HttpStatusCode.Accepted, Describe(cancel));
            using var cancelDocument = JsonDocument.Parse(cancel.Content);
            GetPayload(cancelDocument.RootElement)
                .GetProperty("cancellationRequested")
                .GetBoolean()
                .Should()
                .BeTrue();
        }

        private static DataProtectionAuthorizationTarget CreateTarget()
        {
            return new DataProtectionAuthorizationTarget(
                "integration",
                $"ledger-{Guid.NewGuid():N}",
                "legacy");
        }

        private async Task<DataProtectionPlanConfirmation> CreateAuthorizedPlanAsync(
            DataProtectionAuthorizationTarget target)
        {
            var response = await _client.ExecuteAsync(Authenticate(
                CreatePlanRequest(target),
                AdministratorRole,
                FreshAuthTime()));

            response.StatusCode.Should().Be(HttpStatusCode.OK, Describe(response));
            response.Content.Should().NotBeNullOrWhiteSpace("a successful plan must return its immutable binding");

            using var document = JsonDocument.Parse(response.Content);
            var payload = GetPayload(document.RootElement);
            return new DataProtectionPlanConfirmation(
                payload.GetProperty("planId").GetGuid(),
                payload.GetProperty("planFingerprint").GetString(),
                payload.GetProperty("confirmationToken").GetString());
        }

        private static RestRequest CreatePlanRequest(DataProtectionAuthorizationTarget target)
        {
            return new RestRequest(PlansEndpoint, Method.Post)
                .AddJsonBody(new
                {
                    operationKind = "ledger-reset",
                    target.EnvironmentId,
                    target.LedgerId,
                    target.LedgerGenerationId
                });
        }

        private static RestRequest CreateStartRequest(
            DataProtectionPlanConfirmation plan,
            DataProtectionAuthorizationTarget target,
            string operationKind = "ledger-reset")
        {
            return new RestRequest(OperationsEndpoint, Method.Post)
                .AddJsonBody(new
                {
                    plan.PlanId,
                    operationKind,
                    target.EnvironmentId,
                    target.LedgerId,
                    target.LedgerGenerationId,
                    plan.PlanFingerprint,
                    plan.ConfirmationToken
                });
        }

        private static RestRequest Authenticate(
            RestRequest request,
            string role,
            long authTime,
            string scope = AdministrativeScope,
            string subject = "integration-administrator")
        {
            return request
                .AddHeader(DataProtectionTestAuthenticationHandler.SubjectHeader, subject)
                .AddHeader(DataProtectionTestAuthenticationHandler.RoleHeader, role)
                .AddHeader(DataProtectionTestAuthenticationHandler.ScopeHeader, scope)
                .AddHeader(
                    DataProtectionTestAuthenticationHandler.AuthTimeHeader,
                    authTime.ToString(CultureInfo.InvariantCulture));
        }

        private static long FreshAuthTime()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static JsonElement GetPayload(JsonElement root)
        {
            return root.TryGetProperty("payload", out var wrappedPayload) ? wrappedPayload : root;
        }

        private static bool IsSafeClientRejection(HttpStatusCode statusCode)
        {
            var numericStatus = (int)statusCode;
            return numericStatus >= 400 && numericStatus < 500 && statusCode != HttpStatusCode.NotFound;
        }

        private async Task AssertOperationSnapshotUnchangedAsync(IReadOnlyCollection<string> expected)
        {
            var actual = await ReadOperationSnapshotAsync(TestContext.CurrentContext.CancellationToken);
            actual.Should().Equal(expected, "a rejected administrative request must not create or mutate an operation");
        }

        private Task<IReadOnlyCollection<string>> ReadOperationSnapshotAsync(
            CancellationToken cancellationToken)
        {
            return DataProtectionOperationSnapshotReader.ReadAsync(TestContainers, cancellationToken);
        }

        private static string Describe(RestResponse response)
        {
            return response is null
                ? "Response was null."
                : $"HTTP {(int)response.StatusCode} {response.StatusCode}; content='{response.Content}'; error='{response.ErrorMessage}'";
        }
    }
}
