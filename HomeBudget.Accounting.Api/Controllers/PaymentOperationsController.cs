using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.Operations.Requests;
using HomeBudget.Accounting.Api.Models.Operations.Responses;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Accounts;
using HomeBudget.Components.Accounts.Extensions;
using HomeBudget.Components.Operations;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentOperationsWithPaymentAccountId, Name = Endpoints.PaymentOperations)]
    [ApiController]
    public class PaymentOperationsController(IOperationFactory operationFactory) : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<PaymentOperation>> GetPaymentOperations(string paymentAccountId)
        {
            var paymentAccountOperations = MockOperationsStore.PaymentOperations
                .Where(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new Result<IReadOnlyCollection<PaymentOperation>>(paymentAccountOperations);
        }

        [HttpGet("byId/{operationId}")]
        public Result<PaymentOperation> GetOperationById(string paymentAccountId, string operationId)
        {
            var operationById = MockOperationsStore.PaymentOperations
                .Where(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase))
                .SingleOrDefault(c => string.Equals(c.Key.ToString(), operationId, StringComparison.OrdinalIgnoreCase));

            return operationById == null
                ? new Result<PaymentOperation>(isSucceeded: false, message: $"The operation with '{operationId}' hasn't been found")
                : new Result<PaymentOperation>(payload: operationById);
        }

        [HttpPost]
        public Result<CreateOperationResponse> CreateNewOperation(string paymentAccountId, [FromBody] CreateOperationRequest request)
        {
            var paymentAccount = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(paymentAccountId)));

            if (paymentAccount == null)
            {
                return new Result<CreateOperationResponse>(isSucceeded: false, message: $"The payment account '{paymentAccountId}' doesn't exist");
            }

            var newOperation = operationFactory.Create(
                request.Amount,
                request.Comment,
                request.CategoryId,
                request.ContractorId,
                paymentAccountId);

            MockOperationsStore.PaymentOperations.Add(newOperation);

            paymentAccount.SyncBalanceOnCreate(newOperation);

            var response = new CreateOperationResponse
            {
                PaymentAccountBalance = paymentAccount.Balance,
                PaymentAccountId = paymentAccountId,
                PaymentOperationId = newOperation.Key.ToString()
            };

            return new Result<CreateOperationResponse>(response, isSucceeded: true);
        }

        [HttpDelete("{operationId}")]
        public Result<RemoveOperationResponse> DeleteById(string paymentAccountId, string operationId)
        {
            if (!Guid.TryParse(operationId, out var targetGuid))
            {
                return new Result<RemoveOperationResponse>(isSucceeded: false, message: $"Invalid '{nameof(operationId)}' has been provided");
            }

            var operationForDelete = MockOperationsStore.PaymentOperations
                .Where(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(p => p.Key == targetGuid);

            var isRemoveSuccessful = MockOperationsStore.PaymentOperations.Remove(operationForDelete);

            var paymentAccount = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(paymentAccountId)));

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
                ContractorId = Guid.Parse(request.ContractorId)
            };

            var elementForReplaceIndex = MockOperationsStore.PaymentOperations
                .FindIndex(p => p.Key.CompareTo(requestOperationGuid) == 0);

            if (elementForReplaceIndex == -1)
            {
                return new Result<UpdateOperationResponse>(isSucceeded: false, message: $"A operation with guid: '{nameof(operationId)}' hasn't been found");
            }

            var originOperation = MockOperationsStore.PaymentOperations[elementForReplaceIndex];

            MockOperationsStore.PaymentOperations[elementForReplaceIndex] = updatedOperation;

            var paymentAccount = MockAccountsStore.PaymentAccounts.Find(pa => pa.Key.Equals(Guid.Parse(paymentAccountId)));

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
