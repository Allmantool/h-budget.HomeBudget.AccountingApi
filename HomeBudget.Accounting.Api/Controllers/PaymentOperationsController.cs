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
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentOperationsByPaymentAccountId, Name = Endpoints.PaymentOperations)]
    [ApiController]
    public class PaymentOperationsController(
        IMapper mapper,
        IPaymentOperationsService paymentOperationsService,
        IOutboxPaymentStatusService outboxPaymentStatusService,
        IIdempotencyPreflightService idempotencyPreflightService
        ) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<Result<CreateOperationResponse>>> CreateNewOperationAsync(
            string paymentAccountId,
            [FromBody] CreateOperationRequest request,
            CancellationToken token = default)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<CreateOperationResponse>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);
            var preflight = await idempotencyPreflightService.GetIdempotencyPreflightAsync(
                targetAccountGuid,
                PaymentCommandTypes.Create,
                PaymentCommandFingerprint.Create(PaymentCommandTypes.Create, targetAccountGuid, null, operationPayload),
                Request.Headers["Idempotency-Key"].ToString());

            if (preflight.IsConflict)
            {
                return Conflict(Result<CreateOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<CreateOperationResponse>.Succeeded(CreateOperationResponse.From(paymentAccountId, preflight.ExistingCommand, true));
            }

            var createResponseResult = await paymentOperationsService.CreateAsync(targetAccountGuid, operationPayload, preflight.Context, token);

            var response = new CreateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = createResponseResult.Payload.ToString(),
                CommandId = preflight.Context?.CommandId,
                Status = PaymentCommandStatus.Accepted.ToString(),
                IsDuplicate = preflight.Context?.WasAlreadyAccepted ?? false
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
                Request.Headers["Idempotency-Key"].ToString());

            if (preflight.IsConflict)
            {
                return Conflict(Result<RemoveOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<RemoveOperationResponse>.Succeeded(RemoveOperationResponse.From(paymentAccountId, preflight.ExistingCommand, true));
            }

            var removeResponseResult = await paymentOperationsService.RemoveAsync(targetAccountGuid, targetOperationGuid, preflight.Context, token);

            var response = new RemoveOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = removeResponseResult.Payload.ToString(),
                CommandId = preflight.Context?.CommandId,
                Status = PaymentCommandStatus.Accepted.ToString(),
                IsDuplicate = preflight.Context?.WasAlreadyAccepted ?? false
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
                Request.Headers["Idempotency-Key"].ToString());

            if (preflight.IsConflict)
            {
                return Conflict(Result<UpdateOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<UpdateOperationResponse>.Succeeded(UpdateOperationResponse.From(paymentAccountId, operationId, preflight.ExistingCommand, true));
            }

            var updateResponseResult = await paymentOperationsService.UpdateAsync(targetAccountGuid, targetOperationGuid, operationPayload, preflight.Context, token);

            var response = new UpdateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = updateResponseResult.Payload == Guid.Empty
                    ? operationId
                    : updateResponseResult.Payload.ToString(),
                CommandId = preflight.Context?.CommandId,
                Status = PaymentCommandStatus.Accepted.ToString(),
                IsDuplicate = preflight.Context?.WasAlreadyAccepted ?? false
            };

            return updateResponseResult.IsSucceeded
                ? Result<UpdateOperationResponse>.Succeeded(response)
                : Result<UpdateOperationResponse>.Failure(updateResponseResult.StatusMessage);
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

            var command = await outboxPaymentStatusService.GetCommandAsync(targetAccountGuid, commandId);
            if (command is null)
            {
                return NotFound(Result<PaymentCommandStatusResponse>.Failure($"The payment command '{commandId}' hasn't been found."));
            }

            return Result<PaymentCommandStatusResponse>.Succeeded(new PaymentCommandStatusResponse
            {
                CommandId = command.CommandId,
                PaymentOperationId = command.PaymentOperationId.ToString(),
                CommandType = command.CommandType,
                Status = command.Status.ToString(),
                AcceptedAt = command.AcceptedUtc,
                PublishedAt = command.PublishedUtc,
                PersistedAt = command.PersistedUtc,
                ProjectedAt = command.ProjectedUtc
            });
        }
    }
}
