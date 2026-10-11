using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentOperationsByPaymentAccountId, Name = Endpoints.PaymentOperations)]
    [ApiController]
    public class PaymentOperationsController(
        IMapper mapper,
        IPaymentOperationsService paymentOperationsService,
        IOutboxPaymentStatusService outboxPaymentStatusService,
        IIdempotencyPreflightService idempotencyPreflightService,
        IActiveLedgerGenerationResolver activeGenerationResolver
        ) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<Result<CreateOperationResponse>>> CreateNewOperationAsync(
            string paymentAccountId,
            [FromBody] CreateOperationRequest request,
            CancellationToken token = default)
        {
            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<CreateOperationResponse>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);
            var preflight = await idempotencyPreflightService.GetIdempotencyPreflightAsync(
                targetAccountGuid,
                PaymentCommandTypes.Create,
                PaymentCommandFingerprint.Create(PaymentCommandTypes.Create, targetAccountGuid, null, operationPayload),
                Request.Headers["Idempotency-Key"].ToString(),
                generation);

            if (preflight.IsConflict)
            {
                return Conflict(Result<CreateOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<CreateOperationResponse>.Succeeded(CreateOperationResponse.From(paymentAccountId, preflight.ExistingCommand, true, generation));
            }

            Result<Guid> createResponseResult;
            try
            {
                createResponseResult = await paymentOperationsService.CreateAsync(targetAccountGuid, operationPayload, preflight.Context, generation, token);
            }
            catch (LedgerGenerationChangedException)
            {
                var currentGeneration = await activeGenerationResolver.GetActiveAsync(token);
                SetGenerationHeader(currentGeneration);
                return StaleGeneration(currentGeneration);
            }

            var response = new CreateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = createResponseResult.Payload.ToString(),
                CommandId = preflight.Context?.CommandId,
                Status = PaymentCommandStatus.Accepted.ToString(),
                IsDuplicate = preflight.Context?.WasAlreadyAccepted ?? false,
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                GenerationToken = generation.GenerationToken
            };

            if (createResponseResult.IsSucceeded)
            {
                return Result<CreateOperationResponse>.Succeeded(response);
            }

            if (preflight.Context?.WasAlreadyAccepted == true)
            {
                return Conflict(Result<CreateOperationResponse>.Failure(createResponseResult.StatusMessage));
            }

            return Result<CreateOperationResponse>.Failure(createResponseResult.StatusMessage);
        }

        [HttpDelete("{operationId}")]
        public async Task<ActionResult<Result<RemoveOperationResponse>>> DeleteByIdAsync(
            string paymentAccountId,
            string operationId,
            CancellationToken token = default)
        {
            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<RemoveOperationResponse>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            if (!Guid.TryParse(operationId, out var targetOperationGuid))
            {
                return Result<RemoveOperationResponse>.Failure($"Invalid payment operation '{operationId}' has been provided");
            }

            var preflight = await idempotencyPreflightService.GetIdempotencyPreflightAsync(
                targetAccountGuid,
                PaymentCommandTypes.Delete,
                PaymentCommandFingerprint.CreateDelete(PaymentCommandTypes.Delete, targetAccountGuid, targetOperationGuid),
                Request.Headers["Idempotency-Key"].ToString(),
                generation);

            if (preflight.IsConflict)
            {
                return Conflict(Result<RemoveOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<RemoveOperationResponse>.Succeeded(RemoveOperationResponse.From(paymentAccountId, preflight.ExistingCommand, true, generation));
            }

            Result<Guid> removeResponseResult;
            try
            {
                removeResponseResult = await paymentOperationsService.RemoveAsync(targetAccountGuid, targetOperationGuid, preflight.Context, generation, token);
            }
            catch (LedgerGenerationChangedException)
            {
                var currentGeneration = await activeGenerationResolver.GetActiveAsync(token);
                SetGenerationHeader(currentGeneration);
                return StaleGeneration(currentGeneration);
            }

            var response = new RemoveOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = removeResponseResult.Payload.ToString(),
                CommandId = preflight.Context?.CommandId,
                Status = PaymentCommandStatus.Accepted.ToString(),
                IsDuplicate = preflight.Context?.WasAlreadyAccepted ?? false,
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                GenerationToken = generation.GenerationToken
            };

            if (removeResponseResult.IsSucceeded)
            {
                return Result<RemoveOperationResponse>.Succeeded(response);
            }

            return Result<RemoveOperationResponse>.Failure(removeResponseResult.StatusMessage);
        }

        [HttpPatch("{operationId}")]
        public async Task<ActionResult<Result<UpdateOperationResponse>>> UpdateAsync(
            string paymentAccountId,
            string operationId,
            [FromBody] UpdateOperationRequest request,
            CancellationToken token = default)
        {
            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<UpdateOperationResponse>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            if (!Guid.TryParse(operationId, out var targetOperationGuid))
            {
                return Result<UpdateOperationResponse>.Failure($"Invalid payment operation '{operationId}' has been provided");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);
            var preflight = await idempotencyPreflightService.GetIdempotencyPreflightAsync(
                targetAccountGuid,
                PaymentCommandTypes.Update,
                PaymentCommandFingerprint.Create(PaymentCommandTypes.Update, targetAccountGuid, targetOperationGuid, operationPayload),
                Request.Headers["Idempotency-Key"].ToString(),
                generation);

            if (preflight.IsConflict)
            {
                return Conflict(Result<UpdateOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<UpdateOperationResponse>.Succeeded(UpdateOperationResponse.From(paymentAccountId, operationId, preflight.ExistingCommand, true, generation));
            }

            Result<Guid> updateResponseResult;
            try
            {
                updateResponseResult = await paymentOperationsService.UpdateAsync(targetAccountGuid, targetOperationGuid, operationPayload, preflight.Context, generation, token);
            }
            catch (LedgerGenerationChangedException)
            {
                var currentGeneration = await activeGenerationResolver.GetActiveAsync(token);
                SetGenerationHeader(currentGeneration);
                return StaleGeneration(currentGeneration);
            }

            var response = new UpdateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = updateResponseResult.Payload == Guid.Empty
                    ? operationId
                    : updateResponseResult.Payload.ToString(),
                CommandId = preflight.Context?.CommandId,
                Status = PaymentCommandStatus.Accepted.ToString(),
                IsDuplicate = preflight.Context?.WasAlreadyAccepted ?? false,
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                GenerationToken = generation.GenerationToken
            };

            return updateResponseResult.IsSucceeded
                ? Result<UpdateOperationResponse>.Succeeded(response)
                : Result<UpdateOperationResponse>.Failure(updateResponseResult.StatusMessage);
        }

        [HttpGet("generation")]
        public async Task<ActionResult<Result<LedgerGenerationResponse>>> GetActiveGenerationAsync(
            CancellationToken token = default)
        {
            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            return Result<LedgerGenerationResponse>.Succeeded(LedgerGenerationResponse.From(generation));
        }

        [HttpGet("commands/{commandId}")]
        public async Task<ActionResult<Result<PaymentCommandStatusResponse>>> GetCommandStatusAsync(
            string paymentAccountId,
            string commandId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid) || string.IsNullOrWhiteSpace(commandId))
            {
                return Result<PaymentCommandStatusResponse>.Failure("Invalid payment command route identifiers have been provided.");
            }

            var generation = await activeGenerationResolver.GetActiveAsync();
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            var command = await outboxPaymentStatusService.GetCommandAsync(targetAccountGuid, commandId, generation);
            if (command is null)
            {
                return NotFound(Result<PaymentCommandStatusResponse>.Failure($"The payment command '{commandId}' hasn't been found."));
            }

            return Result<PaymentCommandStatusResponse>.Succeeded(new PaymentCommandStatusResponse
            {
                CommandId = command.CommandId,
                LedgerId = command.LedgerId,
                LedgerGenerationId = command.LedgerGenerationId,
                GenerationToken = generation.GenerationToken,
                PaymentOperationId = command.PaymentOperationId.ToString(),
                CommandType = command.CommandType,
                Status = command.Status.ToString(),
                AcceptedAt = command.AcceptedUtc,
                PublishedAt = command.PublishedUtc,
                PersistedAt = command.PersistedUtc,
                ProjectedAt = command.ProjectedUtc
            });
        }

        private bool IsStalePrecondition(LedgerGenerationScope generation)
        {
            var expected = Request.Headers.IfMatch.ToString();
            if (string.IsNullOrWhiteSpace(expected))
            {
                return generation.GenerationNumber != 0;
            }

            return !string.Equals(expected, generation.GenerationToken, StringComparison.Ordinal);
        }

        private ObjectResult StaleGeneration(LedgerGenerationScope generation)
        {
            return StatusCode(
                412,
                new Result<LedgerGenerationResponse>(
                    LedgerGenerationResponse.From(generation),
                    "The ledger generation precondition is stale or missing.",
                    false));
        }

        private void SetGenerationHeader(LedgerGenerationScope generation)
        {
            Response.Headers.ETag = generation.GenerationToken;
        }
    }
}
