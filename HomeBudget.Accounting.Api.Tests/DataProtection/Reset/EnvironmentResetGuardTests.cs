#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using HomeBudget.Accounting.Infrastructure.DataProtection.Reset;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Reset
{
    [TestFixture]
    internal sealed class EnvironmentResetGuardTests
    {
        [TestCase("localhost", "storage-resource-1", "environment-target-not-positively-identified")]
        [TestCase("disposable-host-resource-1", "localhost", "environment-target-not-positively-identified")]
        [TestCase("docker", "storage-resource-1", "environment-target-not-positively-identified")]
        public async Task Validate_WithNameOnlyIdentity_Rejects(
            string hostIdentity,
            string storageIdentity,
            string expectedCode)
        {
            var plan = ValidPlan(hostIdentity, storageIdentity);

            var validate = () => CreateGuard(plan).ValidateAsync(plan, CancellationToken.None);

            await validate.Should().ThrowAsync<EnvironmentResetException>()
                .Where(error => error.Code == expectedCode);
        }

        [Test]
        public async Task Validate_WithProviderReportingSharedResource_Rejects()
        {
            var valid = ValidPlan("disposable-host-resource-1", "disposable-storage-resource-1");
            var verification = ValidVerification(valid);
            var shared = verification.Resources[0] with { IsDedicated = false };
            var verifier = new StubVerifier(verification with { Resources = [shared] });

            var validate = () => new EnvironmentResetGuard(verifier).ValidateAsync(valid, CancellationToken.None);

            await validate.Should().ThrowAsync<EnvironmentResetException>()
                .Where(error => error.Code == "environment-resource-not-dedicated");
        }

        [Test]
        public async Task Validate_WithChangedEnvironmentMarker_RejectsStaleFingerprint()
        {
            var valid = ValidPlan("disposable-host-resource-1", "disposable-storage-resource-1");
            var changed = valid with { EnvironmentMarker = "another-environment" };

            var validate = () => CreateGuard(valid).ValidateAsync(changed, CancellationToken.None);

            await validate.Should().ThrowAsync<EnvironmentResetException>()
                .Where(error => error.Code == "environment-plan-fingerprint-mismatch");
        }

        [Test]
        public async Task Validate_WithProviderVerifiedDedicatedOwnedResources_AcceptsExactPlan()
        {
            var plan = ValidPlan("disposable-host-resource-1", "disposable-storage-resource-1");

            var result = await CreateGuard(plan).ValidateAsync(plan, CancellationToken.None);

            result.Should().BeSameAs(plan);
        }

        private static EnvironmentResetPlan ValidPlan(string hostIdentity, string storageIdentity)
        {
            var resource = new EnvironmentResetResource(
                "disposable-sql-resource-1",
                "sql");
            var draft = new EnvironmentResetPlan(
                Guid.NewGuid(),
                "disposable-environment",
                "home-ledger-disposable-marker-v1",
                hostIdentity,
                storageIdentity,
                new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 5, 10, 2, 0, TimeSpan.Zero),
                TimeSpan.FromMinutes(15),
                Guid.NewGuid(),
                true,
                true,
                new DateTimeOffset(2026, 10, 5, 10, 3, 0, TimeSpan.Zero),
                new Dictionary<string, long> { ["ledgers"] = 2 },
                [resource],
                ["administrator-access", "deployment-configuration", "protected-audit"],
                string.Empty);
            return draft with { PlanFingerprint = EnvironmentResetPlanBinding.ComputeFingerprint(draft) };
        }

        private static EnvironmentResetGuard CreateGuard(EnvironmentResetPlan plan) =>
            new(new StubVerifier(ValidVerification(plan)));

        private static EnvironmentResetTargetVerification ValidVerification(EnvironmentResetPlan plan) =>
            new(
                "disposable-test-inventory-provider",
                plan.ConfirmedUtc.AddMinutes(-1),
                plan.PlanFingerprint,
                plan.EnvironmentMarker,
                plan.HostResourceId,
                plan.StorageResourceId,
                [new EnvironmentResetVerifiedResource(
                    plan.Resources[0].ResourceId,
                    IsDedicated: true,
                    IsOwnedByHomeLedger: true,
                    ResolvedStorageIdentity: plan.StorageResourceId,
                    OwnershipProofSha256: Hash("provider-owned-evidence"))]);

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private sealed class StubVerifier(EnvironmentResetTargetVerification verification)
            : IEnvironmentResetTargetVerifier
        {
            public Task<EnvironmentResetTargetVerification> InspectAsync(
                EnvironmentResetPlan plan,
                CancellationToken cancellationToken) => Task.FromResult(verification);
        }
    }
}
