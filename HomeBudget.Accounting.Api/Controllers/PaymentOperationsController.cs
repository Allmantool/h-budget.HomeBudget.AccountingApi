using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Models.Operation;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route("payment-operations/{paymentAccountId}")]
    [ApiController]
    public class PaymentOperationsController(IOperationFactory operationFactory) : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<PaymentOperation>> GetPaymentOperations(string paymentAccountId)
        {
            var paymentAccountOperations = MockStore.PaymentOperations
                .Where(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new Result<IReadOnlyCollection<PaymentOperation>>(paymentAccountOperations);
        }

        [HttpGet("byId/{operationId}")]
        public Result<PaymentOperation> GetOperationById(string paymentAccountId, string operationId)
        {
            var operationById = MockStore.PaymentOperations
                .Where(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase))
                .SingleOrDefault(c => string.Equals(c.Key.ToString(), operationId, StringComparison.OrdinalIgnoreCase));

            return operationById == null
                ? new Result<PaymentOperation>(isSucceeded: false, message: $"The operation with '{operationId}' hasn't been found")
                : new Result<PaymentOperation>(payload: operationById);
        }

        [HttpPost]
        public Result<string> CreateNewOperation(string paymentAccountId, [FromBody] CreateOperationRequest request)
        {
            var isPaymentAccountExists = MockStore.PaymentOperations
                .Exists(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase));

            var newOperation = operationFactory.Create(
                request.Amount,
                request.Comment,
                request.CategoryId,
                request.ContractorId,
                paymentAccountId);

            if (!isPaymentAccountExists)
            {
                return new Result<string>(isSucceeded: false, message: $"The payment account '{paymentAccountId}' doesn't exist");
            }

            MockStore.PaymentOperations.Add(newOperation);

            return new Result<string>(newOperation.Key.ToString());
        }

        [HttpDelete("{operationId}")]
        public Result<bool> DeleteById(string paymentAccountId, string operationId)
        {
            if (!Guid.TryParse(operationId, out var targetGuid))
            {
                return new Result<bool>(isSucceeded: false, message: $"Invalid '{nameof(operationId)}' has been provided");
            }

            var operationForDelete = MockStore.PaymentOperations
                .Where(op => string.Equals(op.PaymentAccountId.ToString(), paymentAccountId, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(p => p.Key == targetGuid);

            var isRemoveSuccessful = MockStore.PaymentOperations.Remove(operationForDelete);

            return new Result<bool>(payload: isRemoveSuccessful, isSucceeded: isRemoveSuccessful);
        }

        [HttpPatch("{operationId}")]
        public Result<string> Update(string paymentAccountId, string operationId, [FromBody] UpdateOperationRequest request)
        {
            if (!Guid.TryParse(operationId, out var requestOperationGuid))
            {
                return new Result<string>(isSucceeded: false, message: $"Invalid '{nameof(operationId)}' has been provided");
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

            var elementForReplaceIndex = MockStore.PaymentOperations
                .FindIndex(p => p.Key.CompareTo(requestOperationGuid) == 0);

            if (elementForReplaceIndex == -1)
            {
                return new Result<string>(isSucceeded: false, message: $"A operation with guid: '{nameof(operationId)}' hasn't been found");
            }

            MockStore.PaymentOperations[elementForReplaceIndex] = updatedOperation;

            return new Result<string>(operationId);
        }
    }
}
