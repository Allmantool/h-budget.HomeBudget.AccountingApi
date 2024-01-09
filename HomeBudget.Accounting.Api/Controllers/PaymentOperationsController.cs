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
                return new Result<CreateOperationResponse>(
                    isSucceeded: false,
                    message: $"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);

            var saveResponseResult = await paymentOperationsService.CreateAsync(targetAccountGuid, operationPayload, token);

            var response = new CreateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = saveResponseResult.Payload.ToString()
            };

            return new Result<CreateOperationResponse>(response, isSucceeded: saveResponseResult.IsSucceeded);
        }

        [HttpDelete("{operationId}")]
        public async Task<Result<RemoveOperationResponse>> DeleteByIdAsync(string paymentAccountId, string operationId, CancellationToken token = default)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return new Result<RemoveOperationResponse>(
                    isSucceeded: false,
                    message: $"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var removeResponseResult = await paymentOperationsService.RemoveAsync(targetAccountGuid, Guid.Parse(operationId), token);

            var response = new RemoveOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = removeResponseResult.Payload.ToString()
            };

            return new Result<RemoveOperationResponse>(payload: response, isSucceeded: removeResponseResult.IsSucceeded);
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
                return new Result<UpdateOperationResponse>(
                    isSucceeded: false,
                    message: $"Invalid payment account '{paymentAccountId}' has been provided");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);

            var updateResponseResult = await paymentOperationsService.UpdateAsync(targetAccountGuid, Guid.Parse(operationId), operationPayload, token);

            var response = new UpdateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = updateResponseResult.Payload.ToString()
            };

            return new Result<UpdateOperationResponse>(response, isSucceeded: true);
        }
    }
}
