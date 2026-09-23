using System.Threading;
using System.Threading.Tasks;
using System;

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
    [Route(Endpoints.CrossAccountsTransfer, Name = Endpoints.CrossAccountsTransfer)]
    [ApiController]
    public class CrossAccountTransferController(
        IMapper mapper,
        ICrossAccountsTransferService crossAccountsTransferService) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<Result<CrossAccountsTransferResponse>>> ApplyAsync(
            CrossAccountsTransferRequest request,
            CancellationToken token = default)
        {
            var operationPayload = mapper.Map<CrossAccountsTransferPayload>(request);

            var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var registration = await crossAccountsTransferService.ApplyIdempotentAsync(
                    operationPayload,
                    idempotencyKey,
                    HttpContext.TraceIdentifier,
                    token);
                if (!registration.IsSucceeded)
                {
                    return BadRequest(Result<CrossAccountsTransferResponse>.Failure(registration.StatusMessage));
                }

                if (registration.Payload.IsConflict)
                {
                    return Conflict(Result<CrossAccountsTransferResponse>.Failure(
                        "The idempotency key has already been used for a different transfer request."));
                }

                return Result<CrossAccountsTransferResponse>.Succeeded(new CrossAccountsTransferResponse
                {
                    PaymentOperationId = registration.Payload.TransferId,
                    PaymentAccountIds = [request.Sender, request.Recipient],
                    CommandId = registration.Payload.CommandId,
                    SenderCommandId = registration.Payload.SenderCommandId,
                    RecipientCommandId = registration.Payload.RecipientCommandId,
                    Status = registration.Payload.Status.ToString(),
                    IsDuplicate = registration.Payload.IsDuplicate
                });
            }

            var responseResult = await crossAccountsTransferService.ApplyAsync(operationPayload, token);
            if (!responseResult.IsSucceeded)
            {
                return Result<CrossAccountsTransferResponse>.Failure(responseResult.StatusMessage);
            }

            var response = new CrossAccountsTransferResponse
            {
                PaymentOperationId = responseResult.Payload,
                PaymentAccountIds =
                [
                    request.Sender,
                    request.Recipient
                ]
            };

            return Result<CrossAccountsTransferResponse>.Succeeded(response);
        }

        [HttpGet("{transferId}/commands/{commandId}")]
        public async Task<ActionResult<Result<TransferCommandStatusResponse>>> GetCommandStatusAsync(
            string transferId,
            string commandId)
        {
            if (!Guid.TryParse(transferId, out var targetTransferId) || string.IsNullOrWhiteSpace(commandId))
            {
                return BadRequest(Result<TransferCommandStatusResponse>.Failure("Invalid transfer command route identifiers."));
            }

            var command = await crossAccountsTransferService.GetCommandAsync(targetTransferId, commandId);
            return command is null
                ? NotFound(Result<TransferCommandStatusResponse>.Failure("The transfer command has not been found."))
                : Result<TransferCommandStatusResponse>.Succeeded(ToStatusResponse(command));
        }

        [HttpGet("byId/{transferId}")]
        public async Task<ActionResult<Result<TransferCommandStatusResponse>>> GetByIdAsync(string transferId)
        {
            if (!Guid.TryParse(transferId, out var targetTransferId))
            {
                return BadRequest(Result<TransferCommandStatusResponse>.Failure("Invalid transfer identifier."));
            }

            var command = await crossAccountsTransferService.GetByIdAsync(targetTransferId);
            return command is null
                ? NotFound(Result<TransferCommandStatusResponse>.Failure("The transfer has not been found."))
                : Result<TransferCommandStatusResponse>.Succeeded(ToStatusResponse(command));
        }

        private static TransferCommandStatusResponse ToStatusResponse(TransferCommandRecord command)
        {
            var status = TransferCommandLifecycle.Evaluate(command);
            return new TransferCommandStatusResponse
            {
                TransferId = command.TransferId,
                CommandId = command.CommandId,
                Status = status.ToString(),
                SenderAccountId = command.SenderAccountId,
                RecipientAccountId = command.RecipientAccountId,
                SenderOperationId = command.SenderOperationId,
                RecipientOperationId = command.RecipientOperationId,
                SenderAmount = command.SenderAmount,
                RecipientAmount = command.RecipientAmount,
                SenderCurrency = command.SenderCurrency,
                RecipientCurrency = command.RecipientCurrency,
                OperationDate = command.OperationDate,
                SourceReference = command.SourceReference,
                AcceptedAt = command.AcceptedUtc,
                PublishedAt = BothReached(command.SenderPublishedUtc, command.RecipientPublishedUtc),
                PersistedAt = BothReached(command.SenderPersistedUtc, command.RecipientPersistedUtc),
                ProjectedAt = BothReached(command.SenderProjectedUtc, command.RecipientProjectedUtc),
                Failure = status == PaymentCommandStatus.Failed
                    ? command.SenderError ?? command.RecipientError
                    : null
            };
        }

        private static DateTime? BothReached(DateTime? first, DateTime? second) =>
            first.HasValue && second.HasValue
                ? first > second ? first : second
                : null;

        [HttpDelete]
        public async Task<Result<CrossAccountsTransferResponse>> RemoveAsync(
            RemoveTransferRequest request,
            CancellationToken token = default)
        {
            var removeTransferPayload = mapper.Map<RemoveTransferPayload>(request);

            var responseResult = await crossAccountsTransferService.RemoveAsync(removeTransferPayload, token);
            if (!responseResult.IsSucceeded)
            {
                return Result<CrossAccountsTransferResponse>.Failure(responseResult.StatusMessage);
            }

            var response = new CrossAccountsTransferResponse
            {
                PaymentOperationId = removeTransferPayload.TransferOperationId,
                PaymentAccountIds = responseResult.Payload
            };

            return Result<CrossAccountsTransferResponse>.Succeeded(response);
        }

        [HttpPatch]
        public async Task<Result<CrossAccountsTransferResponse>> UpdateAsync(
            UpdateTransferRequest request,
            CancellationToken token = default)
        {
            var updateTransferPayload = mapper.Map<UpdateTransferPayload>(request);

            var responseResult = await crossAccountsTransferService.UpdateAsync(updateTransferPayload, token);
            if (!responseResult.IsSucceeded)
            {
                return Result<CrossAccountsTransferResponse>.Failure(responseResult.StatusMessage);
            }

            var response = new CrossAccountsTransferResponse
            {
                PaymentOperationId = responseResult.Payload,
                PaymentAccountIds =
                [
                    request.Sender,
                    request.Recipient
                ]
            };

            return Result<CrossAccountsTransferResponse>.Succeeded(response);
        }
    }
}
