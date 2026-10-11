#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Api.Controllers;
using HomeBudget.Accounting.Api.Models.DataProtection;
using HomeBudget.Accounting.Api.Security;
using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Accounting.Api.Tests.DataProtection.Configuration
{
    [TestFixture]
    internal sealed class DataProtectionControllerReadinessTests
    {
        [Test]
        public void GetCapabilities_WithConfiguredReferencesButUnvalidatedRuntime_ReportsNoExecutableKinds()
        {
            var controller = CreateController(DataProtectionRuntimeReadinessEvidence.Unavailable);

            var response = controller.GetCapabilities().Value.Payload;

            response.Status.Should().Be("blocked");
            response.ExecutableOperationKinds.Should().BeEmpty();
            response.MissingPrerequisites.Should().Contain("backup-provider-not-validated");
        }

        [Test]
        public async Task CreatePlanAsync_WithConfiguredReferencesButUnvalidatedRuntime_DoesNotCreatePlan()
        {
            var administration = new RecordingAdministrationService();
            var controller = CreateController(
                DataProtectionRuntimeReadinessEvidence.Unavailable,
                administration);

            var response = await controller.CreatePlanAsync(
                new CreateDataProtectionPlanRequest { OperationKind = DataProtectionOperationKinds.Backup },
                CancellationToken.None);

            response.Result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            administration.CreatePlanCalls.Should().Be(0);
        }

        [Test]
        public async Task StartAsync_WithConfiguredReferencesButUnvalidatedRuntime_DoesNotStartOperation()
        {
            var administration = new RecordingAdministrationService();
            var controller = CreateController(
                DataProtectionRuntimeReadinessEvidence.Unavailable,
                administration);

            var response = await controller.StartAsync(
                new StartDataProtectionOperationRequest { OperationKind = DataProtectionOperationKinds.Backup },
                CancellationToken.None);

            response.Result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            administration.StartCalls.Should().Be(0);
        }

        [Test]
        public void GetCapabilities_WithValidatedRuntimeAndExecutor_ReportsSupportedKinds()
        {
            var options = ConfiguredRecoveryOptions();
            var controller = CreateController(EvidenceFor(options), recoveryOptions: options);

            var response = controller.GetCapabilities().Value.Payload;

            response.Status.Should().Be("available");
            response.ExecutableOperationKinds.Should().Equal(DataProtectionOperationKinds.Backup);
            response.MissingPrerequisites.Should().BeEmpty();
        }

        private static DataProtectionController CreateController(
            DataProtectionRuntimeReadinessEvidence evidence,
            IDataProtectionAdministrationService? administrationService = null,
            DataProtectionRecoveryOptions? recoveryOptions = null)
        {
            return new DataProtectionController(
                administrationService ?? new RecordingAdministrationService(),
                authorizationService: null,
                Options.Create(new DataProtectionApiOptions
                {
                    Enabled = true,
                    ConfirmationLifetimeMinutes = 5
                }),
                Options.Create(recoveryOptions ?? ConfiguredRecoveryOptions()),
                new BackupExecutor(),
                new FixedRuntimeReadinessProbe(evidence));
        }

        private static DataProtectionRecoveryOptions ConfiguredRecoveryOptions() => new()
        {
            Enabled = true,
            BackupProvider = "configured-provider-reference",
            IndependentStore = "configured-store-reference",
            KeyReference = "configured-key-reference",
            NotificationTarget = "configured-notification-reference",
            RecoveryHostResourceId = "configured-recovery-host-reference"
        };

        private static DataProtectionRuntimeReadinessEvidence EvidenceFor(DataProtectionRecoveryOptions options)
        {
            return new DataProtectionRuntimeReadinessEvidence(
                options.BackupProvider,
                options.IndependentStore,
                options.KeyReference,
                options.NotificationTarget,
                options.RecoveryHostResourceId);
        }

        private sealed class FixedRuntimeReadinessProbe(DataProtectionRuntimeReadinessEvidence evidence)
            : IDataProtectionRuntimeReadinessProbe
        {
            public DataProtectionRuntimeReadinessEvidence GetEvidence() => evidence;
        }

        private sealed class BackupExecutor : IDataProtectionOperationExecutor
        {
            public IReadOnlyCollection<string> SupportedOperationKinds { get; } =
                Array.AsReadOnly([DataProtectionOperationKinds.Backup]);

            public Task ExecuteAsync(
                DataProtectionOperation operation,
                DataProtectionOperationFenceContext fenceContext,
                CancellationToken cancellationToken) => Task.CompletedTask;

            public Task ReconcileAsync(
                DataProtectionOperation operation,
                DataProtectionOperationFenceContext fenceContext,
                CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class RecordingAdministrationService : IDataProtectionAdministrationService
        {
            public int CreatePlanCalls { get; private set; }

            public int StartCalls { get; private set; }

            public Task<DataProtectionAdministrativePlan> CreatePlanAsync(
                DataProtectionAdministrativePlanRequest request,
                TimeSpan confirmationLifetime,
                CancellationToken cancellationToken)
            {
                CreatePlanCalls++;
                throw new InvalidOperationException("The readiness gate should reject before plan creation.");
            }

            public Task<DataProtectionOperationStartResult> StartAsync(
                DataProtectionAdministrativeStartRequest request,
                CancellationToken cancellationToken)
            {
                StartCalls++;
                throw new InvalidOperationException("The readiness gate should reject before operation start.");
            }

            public Task<DataProtectionOperation?> GetAsync(
                Guid operationId,
                CancellationToken cancellationToken) => Task.FromResult<DataProtectionOperation?>(null);

            public Task<bool> RequestCancellationAsync(
                Guid operationId,
                string actorId,
                CancellationToken cancellationToken) => Task.FromResult(false);
        }
    }
}
