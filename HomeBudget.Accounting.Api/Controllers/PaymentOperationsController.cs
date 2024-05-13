using System;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentOperationsByPaymentAccountId, Name = Endpoints.PaymentOperations)]
    [ApiController]
    public class PaymentOperationsController(
        IMapper mapper,
        IPaymentOperationsService paymentOperationsService
        ) : ControllerBase
    {
        [HttpPost]
        public async Task<Result<CreateOperationResponse>> CreateNewOperationAsync(
            string paymentAccountId,
            [FromBody] CreateOperationRequest request,
            CancellationToken token = default)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<CreateOperationResponse>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);

            var createResponseResult = await paymentOperationsService.CreateAsync(targetAccountGuid, operationPayload, token);

            var response = new CreateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = createResponseResult.Payload.ToString()
            };

            if (createResponseResult.IsSucceeded)
            {
                return Result<CreateOperationResponse>.Succeeded(response);
            }

            return Result<CreateOperationResponse>.Failure(createResponseResult.StatusMessage);
        }

        [HttpDelete("{operationId}")]
        public async Task<Result<RemoveOperationResponse>> DeleteByIdAsync(
            string paymentAccountId,
            string operationId,
            CancellationToken token = default)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<RemoveOperationResponse>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var removeResponseResult = await paymentOperationsService.RemoveAsync(targetAccountGuid, Guid.Parse(operationId), token);

            var response = new RemoveOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = removeResponseResult.Payload.ToString()
            };

            if (removeResponseResult.IsSucceeded)
            {
                return Result<RemoveOperationResponse>.Succeeded(response);
            }

            return Result<RemoveOperationResponse>.Failure(removeResponseResult.StatusMessage);
        }

        [HttpPatch("{operationId}")]
        public async Task<Result<UpdateOperationResponse>> UpdateAsync(
            string paymentAccountId,
            string operationId,
            [FromBody] UpdateOperationRequest request,
            CancellationToken token = default)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return Result<UpdateOperationResponse>.Failure($"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);

            var updateResponseResult = await paymentOperationsService.UpdateAsync(targetAccountGuid, Guid.Parse(operationId), operationPayload, token);

            var response = new UpdateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = updateResponseResult.Payload.ToString()
            };

            return Result<UpdateOperationResponse>.Succeeded(response);
        }
    }
}
