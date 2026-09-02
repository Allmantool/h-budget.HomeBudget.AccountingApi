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
        IOutboxPaymentStatusService outboxPaymentStatusService
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
            var preflight = await GetIdempotencyPreflightAsync(
                targetAccountGuid,
                PaymentCommandTypes.Create,
                PaymentCommandFingerprint.Create(PaymentCommandTypes.Create, targetAccountGuid, null, operationPayload));

            if (preflight.IsConflict)
            {
                return Conflict(Result<CreateOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<CreateOperationResponse>.Succeeded(CreateResponse(paymentAccountId, preflight.ExistingCommand, true));
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

            var preflight = await GetIdempotencyPreflightAsync(
                targetAccountGuid,
                PaymentCommandTypes.Delete,
                PaymentCommandFingerprint.CreateDelete(PaymentCommandTypes.Delete, targetAccountGuid, targetOperationGuid));

            if (preflight.IsConflict)
            {
                return Conflict(Result<RemoveOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<RemoveOperationResponse>.Succeeded(CreateRemoveResponse(paymentAccountId, preflight.ExistingCommand, true));
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
            var preflight = await GetIdempotencyPreflightAsync(
                targetAccountGuid,
                PaymentCommandTypes.Update,
                PaymentCommandFingerprint.Create(PaymentCommandTypes.Update, targetAccountGuid, targetOperationGuid, operationPayload));

            if (preflight.IsConflict)
            {
                return Conflict(Result<UpdateOperationResponse>.Failure("The idempotency key has already been used for a different payment command."));
            }

            if (preflight.ExistingCommand is not null)
            {
                return Result<UpdateOperationResponse>.Succeeded(CreateUpdateResponse(paymentAccountId, operationId, preflight.ExistingCommand, true));
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

        private async Task<IdempotencyPreflight> GetIdempotencyPreflightAsync(
            Guid paymentAccountId,
            string commandType,
            string requestFingerprint)
        {
            var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return new IdempotencyPreflight();
            }

            if (idempotencyKey.Length > 200)
            {
                return new IdempotencyPreflight { IsConflict = true };
            }

            var keyHash = PaymentCommandFingerprint.HashIdempotencyKey(idempotencyKey);
            var existingCommand = await outboxPaymentStatusService.GetCommandByIdempotencyKeyAsync(paymentAccountId, keyHash);
            if (existingCommand is not null)
            {
                return new IdempotencyPreflight
                {
                    ExistingCommand = existingCommand,
                    IsConflict = !string.Equals(existingCommand.RequestFingerprint, requestFingerprint, StringComparison.Ordinal)
                };
            }

            return new IdempotencyPreflight
            {
                Context = new PaymentCommandContext
                {
                    IdempotencyKeyHash = keyHash,
                    RequestFingerprint = requestFingerprint,
                    CommandType = commandType
                }
            };
        }

        private static CreateOperationResponse CreateResponse(string paymentAccountId, PaymentCommandRecord command, bool duplicate)
        {
            return new CreateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = command.PaymentOperationId.ToString(),
                CommandId = command.CommandId,
                Status = command.Status.ToString(),
                IsDuplicate = duplicate
            };
        }

        private static RemoveOperationResponse CreateRemoveResponse(string paymentAccountId, PaymentCommandRecord command, bool duplicate)
        {
            return new RemoveOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = command.PaymentOperationId.ToString(),
                CommandId = command.CommandId,
                Status = command.Status.ToString(),
                IsDuplicate = duplicate
            };
        }

        private static UpdateOperationResponse CreateUpdateResponse(string paymentAccountId, string operationId, PaymentCommandRecord command, bool duplicate)
        {
            return new UpdateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = command.PaymentOperationId == Guid.Empty ? operationId : command.PaymentOperationId.ToString(),
                CommandId = command.CommandId,
                Status = command.Status.ToString(),
                IsDuplicate = duplicate
            };
        }

        private sealed record IdempotencyPreflight
        {
            public PaymentCommandContext Context { get; init; }
            public PaymentCommandRecord ExistingCommand { get; init; }
            public bool IsConflict { get; init; }
        }
    }
}
