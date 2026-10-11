using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Services
{
    internal sealed class ProjectionRebuildDataProtectionExecutor(
        ProjectionRebuildRunner runner,
        IActiveLedgerGenerationResolver generationResolver,
        IDataProtectionOperationExecutionControl executionControl)
        : IDataProtectionOperationExecutor
    {
        private static readonly IReadOnlyCollection<string> SupportedKinds =
            Array.AsReadOnly([DataProtectionOperationKinds.ProjectionRebuild]);

        public IReadOnlyCollection<string> SupportedOperationKinds => SupportedKinds;

        public async Task ExecuteAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            EnsureProjectionRebuild(operation);
            var generation = await GetExpectedGenerationAsync(operation, cancellationToken);
            var plan = await runner.LoadManagedPlanAsync(cancellationToken);
            ValidatePlanBinding(operation, plan);
            await executionControl.SetStageAsync(
                fenceContext,
                "rebuilding-projections",
                cancellationToken);
            await runner.ExecuteManagedAsync(
                plan,
                generation,
                token => CanContinueAsync(operation, fenceContext, generation, token),
                cancellationToken);
        }

        public async Task ReconcileAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            CancellationToken cancellationToken)
        {
            EnsureProjectionRebuild(operation);
            if (await executionControl.IsCancellationRequestedAsync(fenceContext, cancellationToken))
            {
                return;
            }

            var generation = await GetExpectedGenerationAsync(operation, cancellationToken);
            var plan = await runner.LoadManagedPlanAsync(cancellationToken);
            ValidatePlanBinding(operation, plan);
            await executionControl.SetStageAsync(
                fenceContext,
                "reconciling-projections",
                cancellationToken);
            await runner.ReconcileManagedAsync(plan, generation, cancellationToken);
        }

        private async Task<bool> CanContinueAsync(
            DataProtectionOperation operation,
            DataProtectionOperationFenceContext fenceContext,
            LedgerGenerationScope expectedGeneration,
            CancellationToken cancellationToken)
        {
            if (await executionControl.IsCancellationRequestedAsync(fenceContext, cancellationToken))
            {
                return false;
            }

            var active = await generationResolver.GetActiveAsync(cancellationToken);
            EnsureSameGeneration(expectedGeneration, active);
            EnsureOperationGeneration(operation, active);
            return true;
        }

        private async Task<LedgerGenerationScope> GetExpectedGenerationAsync(
            DataProtectionOperation operation,
            CancellationToken cancellationToken)
        {
            var active = await generationResolver.GetActiveAsync(cancellationToken);
            EnsureOperationGeneration(operation, active);
            return active;
        }

        private static void ValidatePlanBinding(
            DataProtectionOperation operation,
            Models.ProjectionRebuildPlan plan)
        {
            var expected = ProjectionRebuildPlanBinding.ComputeOperationFingerprint(
                operation.EnvironmentId,
                operation.LedgerId,
                operation.GenerationId,
                plan.InventoryFingerprint);
            if (!string.Equals(expected, operation.PlanFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Projection rebuild operation fingerprint does not bind the immutable scope inventory.");
            }
        }

        private static void EnsureProjectionRebuild(DataProtectionOperation operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (!string.Equals(
                    operation.OperationKind,
                    DataProtectionOperationKinds.ProjectionRebuild,
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Projection rebuild executor cannot execute operation kind '{operation.OperationKind}'.");
            }
        }

        private static void EnsureOperationGeneration(
            DataProtectionOperation operation,
            LedgerGenerationScope generation)
        {
            if (!string.Equals(operation.EnvironmentId, generation.EnvironmentId, StringComparison.Ordinal) ||
                !string.Equals(operation.LedgerId, generation.LedgerId, StringComparison.Ordinal) ||
                !string.Equals(operation.GenerationId, generation.LedgerGenerationId, StringComparison.Ordinal))
            {
                throw new LedgerGenerationChangedException(
                    "Projection rebuild operation no longer targets the active ledger generation.");
            }
        }

        private static void EnsureSameGeneration(
            LedgerGenerationScope expected,
            LedgerGenerationScope active)
        {
            if (expected != active)
            {
                throw new LedgerGenerationChangedException(
                    "Active ledger generation changed while projection rebuild was running.");
            }
        }
    }
}
