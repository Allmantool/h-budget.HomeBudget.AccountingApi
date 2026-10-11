using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using HomeBudget.Accounting.Api.Configuration;
using HomeBudget.Accounting.Api.Filters;
using HomeBudget.Accounting.Api.Models.DataProtection;
using HomeBudget.Accounting.Api.Security;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [AllowDuringMaintenance]
    [ApiController]
    [Authorize(Policy = DataProtectionPolicies.Administrator)]
    [Route("data-protection/v1", Name = "DataProtectionV1")]
    public sealed class DataProtectionController(
        IDataProtectionAdministrationService administrationService,
        IAuthorizationService authorizationService,
        IOptions<DataProtectionApiOptions> options,
        IOptions<DataProtectionRecoveryOptions> recoveryOptions,
        IDataProtectionOperationExecutor operationExecutor,
        IDataProtectionRuntimeReadinessProbe runtimeReadinessProbe) : ControllerBase
    {
        [HttpGet("capabilities")]
        public ActionResult<Result<DataProtectionCapabilityResponse>> GetCapabilities()
        {
            var supportedKinds = operationExecutor.SupportedOperationKinds.ToArray();
            var missingPrerequisites = GetMissingPrerequisites();
            if (supportedKinds.Length == 0)
            {
                missingPrerequisites.Add("operation-executors-not-configured");
            }

            var executableKinds = missingPrerequisites.Count == 0
                ? supportedKinds
                : Array.Empty<string>();

            return Result<DataProtectionCapabilityResponse>.Succeeded(new DataProtectionCapabilityResponse
            {
                Enabled = options.Value.Enabled,
                AuthorizationConfigured = options.Value.Enabled,
                Status = missingPrerequisites.Count == 0 ? "available" : "blocked",
                RecognizedOperationKinds = DataProtectionOperationKinds.All,
                ExecutableOperationKinds = executableKinds,
                MissingPrerequisites = missingPrerequisites.AsReadOnly(),
                SupportsPolling = true,
                SupportsServerSentEvents = false
            });
        }

        [HttpPost("plans")]
        public async Task<ActionResult<Result<DataProtectionPlanResponse>>> CreatePlanAsync(
            [FromBody] CreateDataProtectionPlanRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!IsExecutable(request.OperationKind))
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    Result<DataProtectionPlanResponse>.Failure(
                        "The requested data-protection operation is blocked by missing prerequisites."));
            }

            var actorId = DataProtectionActor.GetId(User);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return Forbid();
            }

            try
            {
                var plan = await administrationService.CreatePlanAsync(
                    new DataProtectionAdministrativePlanRequest
                    {
                        OperationKind = request.OperationKind,
                        EnvironmentId = request.EnvironmentId,
                        LedgerId = request.LedgerId,
                        LedgerGenerationId = request.LedgerGenerationId,
                        ScopeInventoryFingerprint = request.ScopeInventoryFingerprint,
                        ActorId = actorId
                    },
                    TimeSpan.FromMinutes(options.Value.ConfirmationLifetimeMinutes),
                    cancellationToken);
                return Result<DataProtectionPlanResponse>.Succeeded(ToResponse(plan));
            }
            catch (ArgumentException)
            {
                return BadRequest(Result<DataProtectionPlanResponse>.Failure(
                    "The requested data-protection plan target is invalid."));
            }
        }

        [HttpPost("operations")]
        public async Task<ActionResult<Result<DataProtectionOperationResponse>>> StartAsync(
            [FromBody] StartDataProtectionOperationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!IsExecutable(request.OperationKind))
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    Result<DataProtectionOperationResponse>.Failure(
                        "The requested data-protection operation is blocked by missing prerequisites."));
            }

            if (DataProtectionOperationKinds.RequiresRecentAuthentication(request.OperationKind))
            {
                var recent = await authorizationService.AuthorizeAsync(
                    User,
                    resource: null,
                    DataProtectionPolicies.Destructive);
                if (!recent.Succeeded)
                {
                    return Forbid();
                }
            }

            var actorId = DataProtectionActor.GetId(User);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return Forbid();
            }

            try
            {
                var result = await administrationService.StartAsync(
                    new DataProtectionAdministrativeStartRequest
                    {
                        PlanId = request.PlanId,
                        OperationKind = request.OperationKind,
                        EnvironmentId = request.EnvironmentId,
                        LedgerId = request.LedgerId,
                        LedgerGenerationId = request.LedgerGenerationId,
                        PlanFingerprint = request.PlanFingerprint,
                        ConfirmationToken = request.ConfirmationToken,
                        ActorId = actorId
                    },
                    cancellationToken);
                if (result.Outcome != DataProtectionOperationStartOutcome.Accepted)
                {
                    return Conflict(Result<DataProtectionOperationResponse>.Failure(
                        "The plan confirmation is invalid, expired, consumed, or no longer matches the target."));
                }

                var response = ToResponse(result.Operation);
                return Accepted(
                    new Uri($"/data-protection/v1/operations/{response.OperationId:D}", UriKind.Relative),
                    Result<DataProtectionOperationResponse>.Succeeded(response));
            }
            catch (ArgumentException)
            {
                return BadRequest(Result<DataProtectionOperationResponse>.Failure(
                    "The data-protection operation request is invalid."));
            }
        }

        [HttpGet("operations/{operationId:guid}")]
        public async Task<ActionResult<Result<DataProtectionOperationResponse>>> GetOperationAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            var operation = await administrationService.GetAsync(operationId, cancellationToken);
            return operation == null
                ? NotFound(Result<DataProtectionOperationResponse>.Failure("The data-protection operation was not found."))
                : Result<DataProtectionOperationResponse>.Succeeded(ToResponse(operation));
        }

        [HttpPost("operations/{operationId:guid}/cancel")]
        public async Task<ActionResult<Result<DataProtectionOperationResponse>>> CancelAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            var actorId = DataProtectionActor.GetId(User);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return Forbid();
            }

            var operation = await administrationService.GetAsync(operationId, cancellationToken);
            if (operation == null)
            {
                return NotFound(Result<DataProtectionOperationResponse>.Failure(
                    "The data-protection operation was not found."));
            }

            var accepted = await administrationService.RequestCancellationAsync(
                operationId,
                actorId,
                cancellationToken);
            if (!accepted)
            {
                return Conflict(Result<DataProtectionOperationResponse>.Failure(
                    "The data-protection operation no longer accepts cancellation."));
            }

            var updated = await administrationService.GetAsync(operationId, cancellationToken);
            return Accepted(Result<DataProtectionOperationResponse>.Succeeded(ToResponse(updated)));
        }

        private static DataProtectionPlanResponse ToResponse(DataProtectionAdministrativePlan plan)
        {
            return new DataProtectionPlanResponse
            {
                PlanId = plan.PlanId,
                OperationKind = plan.OperationKind,
                EnvironmentId = plan.EnvironmentId,
                LedgerId = plan.LedgerId,
                LedgerGenerationId = plan.LedgerGenerationId,
                ScopeInventoryFingerprint = plan.ScopeInventoryFingerprint,
                PlanFingerprint = plan.PlanFingerprint,
                ConfirmationToken = plan.ConfirmationToken,
                ExpiresUtc = plan.ExpiresUtc
            };
        }

        private static DataProtectionOperationResponse ToResponse(DataProtectionOperation operation)
        {
            return new DataProtectionOperationResponse
            {
                OperationId = operation.OperationId,
                OperationKind = operation.OperationKind,
                EnvironmentId = operation.EnvironmentId,
                LedgerId = operation.LedgerId,
                LedgerGenerationId = operation.GenerationId,
                PlanFingerprint = operation.PlanFingerprint,
                State = operation.StateValue,
                Stage = operation.Stage,
                CancellationRequested = operation.CancellationRequested,
                CreatedUtc = operation.CreatedUtc,
                UpdatedUtc = operation.UpdatedUtc
            };
        }

        private bool IsExecutable(string operationKind)
        {
            return GetMissingPrerequisites().Count == 0 &&
                   operationExecutor.SupportedOperationKinds.Contains(operationKind, StringComparer.Ordinal);
        }

        private System.Collections.Generic.List<string> GetMissingPrerequisites()
        {
            return DataProtectionRecoveryReadiness
                .Evaluate(recoveryOptions.Value, runtimeReadinessProbe.GetEvidence())
                .ToList();
        }
    }
}
