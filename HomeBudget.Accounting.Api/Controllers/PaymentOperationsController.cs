﻿using System;
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
using HomeBudget.Components.Accounts.Extensions;
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
            var paymentAccount = MockAccountsStore.Records.Find(pa => pa.Key.CompareTo(Guid.Parse(paymentAccountId)) == 0);

            if (paymentAccount == null)
            {
                return new Result<CreateOperationResponse>(
                    isSucceeded: false,
                    message: $"The payment account '{paymentAccountId}' doesn't exist");
            }

            var operationPayload = mapper.Map<PaymentOperationPayload>(request);

            var responseResult = await paymentOperationsService.CreateAsync(paymentAccountId, operationPayload, token);

            var response = new CreateOperationResponse
            {
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = responseResult.Payload.ToString()
            };

            return new Result<CreateOperationResponse>(response, isSucceeded: responseResult.IsSucceeded);
        }

        [HttpDelete("{operationId}")]
        public Result<RemoveOperationResponse> DeleteById(string paymentAccountId, string operationId)
        {
            if (!Guid.TryParse(operationId, out var targetGuid))
            {
                return new Result<RemoveOperationResponse>(isSucceeded: false, message: $"Invalid '{nameof(operationId)}' has been provided");
            }

            var operationForDelete = MockOperationsStore.Records
                .Where(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(p => p.Key == targetGuid);

            var isRemoveSuccessful = MockOperationsStore.Records.Remove(operationForDelete);

            var paymentAccount = MockAccountsStore.Records.Find(pa => pa.Key.Equals(Guid.Parse(paymentAccountId)));

            if (paymentAccount == null)
            {
                return new Result<RemoveOperationResponse>(isSucceeded: false, message: $"The payment account '{paymentAccountId}' doesn't exist");
            }

            paymentAccount.SyncBalanceOnDelete(operationForDelete);

            var response = new RemoveOperationResponse
            {
                PaymentAccountBalance = paymentAccount.Balance,
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = operationForDelete.Key.ToString()
            };

            return new Result<RemoveOperationResponse>(payload: response, isSucceeded: isRemoveSuccessful);
        }

        [HttpPatch("{operationId}")]
        public Result<UpdateOperationResponse> Update(string paymentAccountId, string operationId, [FromBody] UpdateOperationRequest request)
        {
            if (!Guid.TryParse(operationId, out var requestOperationGuid))
            {
                return new Result<UpdateOperationResponse>(isSucceeded: false, message: $"Invalid '{nameof(operationId)}' has been provided");
            }

            var updatedOperation = new PaymentOperation
            {
                Key = requestOperationGuid,
                Amount = request.Amount,
                Comment = request.Comment,
                PaymentAccountId = Guid.Parse(paymentAccountId),
                CategoryId = Guid.Parse(request.CategoryId),
                ContractorId = Guid.Parse(request.ContractorId),
                OperationDay = request.OperationDate
            };

            var elementForReplaceIndex = MockOperationsStore.Records.ToList()
                .FindIndex(p => p.Key.CompareTo(requestOperationGuid) == 0);

            if (elementForReplaceIndex == -1)
            {
                return new Result<UpdateOperationResponse>(isSucceeded: false, message: $"A operation with guid: '{nameof(operationId)}' hasn't been found");
            }

            var originOperation = MockOperationsStore.Records[elementForReplaceIndex];

            MockOperationsStore.Records[elementForReplaceIndex] = updatedOperation;

            var paymentAccount = MockAccountsStore.Records.Find(pa => pa.Key.Equals(Guid.Parse(paymentAccountId)));

            paymentAccount.SyncBalanceOnUpdate(originOperation, updatedOperation);

            var response = new UpdateOperationResponse
            {
                PaymentAccountBalance = paymentAccount.Balance,
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = updatedOperation.Key.ToString()
            };

            return new Result<UpdateOperationResponse>(response, isSucceeded: true);
        }
    }
}
