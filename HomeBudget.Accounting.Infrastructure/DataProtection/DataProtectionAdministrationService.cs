using System;
using System.Threading;
using System.Threading.Tasks;

namespace HomeBudget.Accounting.Infrastructure.DataProtection
{
    internal sealed class DataProtectionAdministrationService(
        IDataProtectionPlanStore planStore,
        IDataProtectionOperationStore operationStore,
        TimeProvider timeProvider) : IDataProtectionAdministrationService
    {
        public async Task<DataProtectionAdministrativePlan> CreatePlanAsync(
            DataProtectionAdministrativePlanRequest request,
            TimeSpan confirmationLifetime,
            CancellationToken cancellationToken)
        {
            ValidatePlanRequest(request, confirmationLifetime);
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var scopeKind = GetScopeKind(request.OperationKind);
            var fingerprint = request.OperationKind == DataProtectionOperationKinds.ProjectionRebuild
                ? ProjectionRebuildPlanBinding.ComputeOperationFingerprint(
                    request.EnvironmentId,
                    request.LedgerId,
                    request.LedgerGenerationId,
                    request.ScopeInventoryFingerprint)
                : DataProtectionPlanBinding.ComputeFingerprint(
                    request.OperationKind,
                    request.EnvironmentId,
                    request.LedgerId,
                    request.LedgerGenerationId);
            var confirmationToken = DataProtectionConfirmationToken.Create();
            var plan = new DataProtectionPlanRecord
            {
                PlanId = Guid.NewGuid(),
                OperationKind = request.OperationKind,
                ScopeKind = scopeKind,
                ScopeKey = DataProtectionPlanBinding.ComputeScopeKey(
                    scopeKind,
                    request.EnvironmentId,
                    request.LedgerId,
                    request.LedgerGenerationId),
                EnvironmentId = request.EnvironmentId,
                LedgerId = request.LedgerId,
                GenerationId = request.LedgerGenerationId,
                PlanFingerprint = fingerprint,
                ActorId = request.ActorId,
                ConfirmationTokenHash = DataProtectionPlanBinding.HashConfirmationToken(confirmationToken),
                CreatedUtc = nowUtc,
                ExpiresUtc = nowUtc.Add(confirmationLifetime)
            };

            await planStore.CreateAsync(plan, cancellationToken);
            return ToAdministrativePlan(plan, confirmationToken, request.ScopeInventoryFingerprint);
        }

        public async Task<DataProtectionOperationStartResult> StartAsync(
            DataProtectionAdministrativeStartRequest request,
            CancellationToken cancellationToken)
        {
            ValidateStartRequest(request);
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var scopeKind = GetScopeKind(request.OperationKind);
            var scopeKey = DataProtectionPlanBinding.ComputeScopeKey(
                scopeKind,
                request.EnvironmentId,
                request.LedgerId,
                request.LedgerGenerationId);
            var expectedPlan = new DataProtectionPlanRecord
            {
                PlanId = request.PlanId,
                OperationKind = request.OperationKind,
                ScopeKind = scopeKind,
                ScopeKey = scopeKey,
                EnvironmentId = request.EnvironmentId,
                LedgerId = request.LedgerId,
                GenerationId = request.LedgerGenerationId,
                PlanFingerprint = request.PlanFingerprint,
                ActorId = request.ActorId,
                ConfirmationTokenHash = DataProtectionPlanBinding.HashConfirmationToken(request.ConfirmationToken)
            };
            var operation = new DataProtectionOperationRequest
            {
                OperationId = Guid.NewGuid(),
                OperationKind = request.OperationKind,
                ScopeKind = scopeKind,
                ScopeKey = scopeKey,
                EnvironmentId = request.EnvironmentId,
                LedgerId = request.LedgerId,
                GenerationId = request.LedgerGenerationId,
                PlanFingerprint = request.PlanFingerprint,
                ActorId = request.ActorId,
                CreatedUtc = nowUtc
            };
            return await planStore.TryConsumeAndEnqueueAsync(expectedPlan, operation, cancellationToken);
        }

        public Task<DataProtectionOperation> GetAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            return operationStore.GetAsync(operationId, cancellationToken);
        }

        public Task<bool> RequestCancellationAsync(
            Guid operationId,
            string actorId,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
            return operationStore.RequestCancellationAsync(
                operationId,
                actorId,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
        }

        private static void ValidatePlanRequest(
            DataProtectionAdministrativePlanRequest request,
            TimeSpan confirmationLifetime)
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateBinding(
                request.OperationKind,
                request.EnvironmentId,
                request.LedgerId,
                request.LedgerGenerationId,
                request.ActorId);
            if (request.OperationKind == DataProtectionOperationKinds.ProjectionRebuild)
            {
                ProjectionRebuildPlanBinding.ValidateInventoryFingerprint(request.ScopeInventoryFingerprint);
            }

            if (confirmationLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(confirmationLifetime),
                    "Confirmation lifetime must be positive.");
            }
        }

        private static void ValidateStartRequest(DataProtectionAdministrativeStartRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.PlanId == Guid.Empty)
            {
                throw new ArgumentException("Plan ID is required.", nameof(request));
            }

            ValidateBinding(
                request.OperationKind,
                request.EnvironmentId,
                request.LedgerId,
                request.LedgerGenerationId,
                request.ActorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.PlanFingerprint);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ConfirmationToken);
        }

        private static void ValidateBinding(
            string operationKind,
            string environmentId,
            string ledgerId,
            string generationId,
            string actorId)
        {
            if (!DataProtectionOperationKinds.IsSupported(operationKind))
            {
                throw new ArgumentException("Unsupported data-protection operation kind.", nameof(operationKind));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
            ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
            if (operationKind == DataProtectionOperationKinds.EnvironmentReset)
            {
                if (ledgerId != null || generationId != null)
                {
                    throw new ArgumentException("Environment reset cannot carry a ledger target.");
                }

                return;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(ledgerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(generationId);
        }

        private static string GetScopeKind(string operationKind)
        {
            return operationKind == DataProtectionOperationKinds.EnvironmentReset ? "environment" : "ledger";
        }

        private static DataProtectionAdministrativePlan ToAdministrativePlan(
            DataProtectionPlanRecord plan,
            string confirmationToken,
            string scopeInventoryFingerprint)
        {
            return new DataProtectionAdministrativePlan
            {
                PlanId = plan.PlanId,
                OperationKind = plan.OperationKind,
                EnvironmentId = plan.EnvironmentId,
                LedgerId = plan.LedgerId,
                LedgerGenerationId = plan.GenerationId,
                ScopeInventoryFingerprint = scopeInventoryFingerprint,
                PlanFingerprint = plan.PlanFingerprint,
                ConfirmationToken = confirmationToken,
                ExpiresUtc = plan.ExpiresUtc
            };
        }
    }
}
