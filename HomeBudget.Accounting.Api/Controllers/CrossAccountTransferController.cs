using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.CrossAccountsTransfer, Name = Endpoints.CrossAccountsTransfer)]
    [ApiController]
    public class CrossAccountTransferController(
        IMapper mapper,
        ICrossAccountsTransferService crossAccountsTransferService,
        IActiveLedgerGenerationResolver activeGenerationResolver) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<Result<CrossAccountsTransferResponse>>> ApplyAsync(
            CrossAccountsTransferRequest request,
            CancellationToken token = default)
        {
            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            var operationPayload = mapper.Map<CrossAccountsTransferPayload>(request);

            var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                Result<TransferCommandResult> registration;
                try
                {
                    registration = await crossAccountsTransferService.ApplyIdempotentAsync(
                        operationPayload,
                        idempotencyKey,
                        HttpContext.TraceIdentifier,
                        generation,
                        token);
                }
                catch (LedgerGenerationChangedException)
                {
                    return await CurrentStaleGenerationAsync(token);
                }

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
                    IsDuplicate = registration.Payload.IsDuplicate,
                    LedgerId = generation.LedgerId,
                    LedgerGenerationId = generation.LedgerGenerationId,
                    GenerationToken = generation.GenerationToken
                });
            }

            Result<Guid> responseResult;
            try
            {
                responseResult = await crossAccountsTransferService.ApplyAsync(operationPayload, generation, token);
            }
            catch (LedgerGenerationChangedException)
            {
                return await CurrentStaleGenerationAsync(token);
            }

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
                ],
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                GenerationToken = generation.GenerationToken
            };

            return Result<CrossAccountsTransferResponse>.Succeeded(response);
        }

        [HttpGet("{transferId}/commands/{commandId}")]
        public async Task<ActionResult<Result<TransferCommandStatusResponse>>> GetCommandStatusAsync(
            string transferId,
            string commandId,
            CancellationToken token = default)
        {
            if (!Guid.TryParse(transferId, out var targetTransferId) || string.IsNullOrWhiteSpace(commandId))
            {
                return BadRequest(Result<TransferCommandStatusResponse>.Failure("Invalid transfer command route identifiers."));
            }

            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            var command = await crossAccountsTransferService.GetCommandAsync(targetTransferId, commandId, generation);
            return command is null
                ? NotFound(Result<TransferCommandStatusResponse>.Failure("The transfer command has not been found."))
                : Result<TransferCommandStatusResponse>.Succeeded(ToStatusResponse(command, generation.GenerationToken));
        }

        [HttpGet("byId/{transferId}")]
        public async Task<ActionResult<Result<TransferCommandStatusResponse>>> GetByIdAsync(
            string transferId,
            CancellationToken token = default)
        {
            if (!Guid.TryParse(transferId, out var targetTransferId))
            {
                return BadRequest(Result<TransferCommandStatusResponse>.Failure("Invalid transfer identifier."));
            }

            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            var command = await crossAccountsTransferService.GetByIdAsync(targetTransferId, generation);
            return command is null
                ? NotFound(Result<TransferCommandStatusResponse>.Failure("The transfer has not been found."))
                : Result<TransferCommandStatusResponse>.Succeeded(ToStatusResponse(command, generation.GenerationToken));
        }

        private static TransferCommandStatusResponse ToStatusResponse(TransferCommandRecord command, string generationToken)
        {
            var status = TransferCommandLifecycle.Evaluate(command);
            return new TransferCommandStatusResponse
            {
                TransferId = command.TransferId,
                CommandId = command.CommandId,
                LedgerId = command.LedgerId,
                LedgerGenerationId = command.LedgerGenerationId,
                GenerationToken = generationToken,
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
        public async Task<ActionResult<Result<CrossAccountsTransferResponse>>> RemoveAsync(
            RemoveTransferRequest request,
            CancellationToken token = default)
        {
            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            var removeTransferPayload = mapper.Map<RemoveTransferPayload>(request);

            Result<IEnumerable<Guid>> responseResult;
            try
            {
                responseResult = await crossAccountsTransferService.RemoveAsync(removeTransferPayload, generation, token);
            }
            catch (LedgerGenerationChangedException)
            {
                return await CurrentStaleGenerationAsync(token);
            }

            if (!responseResult.IsSucceeded)
            {
                return Result<CrossAccountsTransferResponse>.Failure(responseResult.StatusMessage);
            }

            var response = new CrossAccountsTransferResponse
            {
                PaymentOperationId = removeTransferPayload.TransferOperationId,
                PaymentAccountIds = responseResult.Payload,
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                GenerationToken = generation.GenerationToken
            };

            return Result<CrossAccountsTransferResponse>.Succeeded(response);
        }

        [HttpPatch]
        public async Task<ActionResult<Result<CrossAccountsTransferResponse>>> UpdateAsync(
            UpdateTransferRequest request,
            CancellationToken token = default)
        {
            var generation = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(generation);
            if (IsStalePrecondition(generation))
            {
                return StaleGeneration(generation);
            }

            var updateTransferPayload = mapper.Map<UpdateTransferPayload>(request);

            Result<Guid> responseResult;
            try
            {
                responseResult = await crossAccountsTransferService.UpdateAsync(updateTransferPayload, generation, token);
            }
            catch (LedgerGenerationChangedException)
            {
                return await CurrentStaleGenerationAsync(token);
            }

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
                ],
                LedgerId = generation.LedgerId,
                LedgerGenerationId = generation.LedgerGenerationId,
                GenerationToken = generation.GenerationToken
            };

            return Result<CrossAccountsTransferResponse>.Succeeded(response);
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

        private async Task<ObjectResult> CurrentStaleGenerationAsync(CancellationToken token)
        {
            var currentGeneration = await activeGenerationResolver.GetActiveAsync(token);
            SetGenerationHeader(currentGeneration);
            return StaleGeneration(currentGeneration);
        }

        private void SetGenerationHeader(LedgerGenerationScope generation)
        {
            Response.Headers.ETag = generation.GenerationToken;
        }
    }
}
