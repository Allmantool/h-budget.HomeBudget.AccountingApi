using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts;
using HomeBudget.Components.Operations;
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
        [HttpGet("byId/{operationId}")]
        public Result<PaymentOperation> GetOperationById(string paymentAccountId, string operationId)
        {
            var operationById = MockOperationsStore.Records
                .Where(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase))
                .SingleOrDefault(c => string.Equals(c.Key.ToString(), operationId, StringComparison.OrdinalIgnoreCase));

            return operationById == null
                ? new Result<PaymentOperation>(isSucceeded: false, message: $"The operation with '{operationId}' hasn't been found")
                : new Result<PaymentOperation>(payload: operationById);
        }

        [HttpPost]
        public async Task<Result<CreateOperationResponse>> CreateNewOperationAsync(
            string paymentAccountId,
            [FromBody] CreateOperationRequest request,
            CancellationToken token = default)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid) || MockAccountsStore.Records.All(pa => pa.Key.CompareTo(targetAccountGuid) != 0))
            {
                return new Result<CreateOperationResponse>(
                    isSucceeded: false,
                    message: $"Invalid payment account '{nameof(targetAccountGuid)}' has been provided");
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
            // TODO: Fluent validation
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid) || MockAccountsStore.Records.All(pa => pa.Key.CompareTo(targetAccountGuid) != 0))
            {
                return new Result<RemoveOperationResponse>(
                    isSucceeded: false,
                    message: $"Invalid payment account '{nameof(targetAccountGuid)}' has been provided");
            }

            var removeResponseResult = await paymentOperationsService.RemoveAsync(targetAccountGuid, Guid.Parse(operationId), token);

            var paymentAccount = MockAccountsStore.Records.Find(pa => pa.Key.CompareTo(targetAccountGuid) == 0);

            var response = new RemoveOperationResponse
            {
                PaymentAccountBalance = paymentAccount.Balance,
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
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid) || MockAccountsStore.Records.All(pa => pa.Key.CompareTo(targetAccountGuid) != 0))
            {
                return new Result<UpdateOperationResponse>(
                    isSucceeded: false,
                    message: $"Invalid payment account '{nameof(targetAccountGuid)}' has been provided");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);

            var updateResponseResult = await paymentOperationsService.UpdateAsync(targetAccountGuid, Guid.Parse(operationId), operationPayload, token);

            var paymentAccount = MockAccountsStore.Records.Find(pa => pa.Key.CompareTo(targetAccountGuid) == 0);

            var response = new UpdateOperationResponse
            {
                PaymentAccountBalance = paymentAccount.Balance,
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = updateResponseResult.Payload.ToString()
            };

            return new Result<UpdateOperationResponse>(response, isSucceeded: true);
        }
    }
}
