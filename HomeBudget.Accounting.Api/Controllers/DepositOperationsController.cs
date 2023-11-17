using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Api.Models.Operation;
using HomeBudget.Accounting.Domain.Services;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route("operations")]
    [ApiController]
    public class DepositOperationsController(IOperationFactory operationFactory) : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<DepositOperation>> GetDepositOperations()
        {
            return new Result<IReadOnlyCollection<DepositOperation>>(MockStore.DepositOperations);
        }

        [HttpGet("byId/{operationId}")]
        public Result<DepositOperation> GetOperationById(string operationId)
        {
            var operationById = MockStore.DepositOperations
                .SingleOrDefault(c => string.Equals(c.Key.ToString(), operationId, StringComparison.OrdinalIgnoreCase));

            return operationById == null
                ? new Result<DepositOperation>(isSucceeded: false, message: $"The operation with '{operationId}' hasn't been found")
                : new Result<DepositOperation>(payload: operationById);
        }

        [HttpPost]
        public Result<string> CreateNewOperation([FromBody] CreateOperationRequest request)
        {
            var newOperation = operationFactory.Create(request.Amount, request.Comment, request.CategoryId, request.ContractorId);

            if (MockStore.DepositOperations.Select(op => op.Key).Contains(newOperation.Key))
            {
                return new Result<string>(isSucceeded: false, message: $"The operation with '{newOperation.Key}' key already exists");
            }

            MockStore.DepositOperations.Add(newOperation);

            return new Result<string>(newOperation.Key.ToString());
        }

        [HttpDelete("{operationId}")]
        public Result<bool> DeleteById(string operationId)
        {
            if (!Guid.TryParse(operationId, out var targetGuid))
            {
                return new Result<bool>(isSucceeded: false, message: $"Invalid {nameof(operationId)} has been provided");
            }

            var operationForDelete = MockStore.DepositOperations.FirstOrDefault(p => p.Key == targetGuid);
            var isRemoveSuccessful = MockStore.DepositOperations.Remove(operationForDelete);

            return new Result<bool>(payload: isRemoveSuccessful, isSucceeded: isRemoveSuccessful);
        }

        [HttpPatch("{operationId}")]
        public Result<string> Update(string operationId, [FromBody] UpdateOperationRequest request)
        {
            if (!Guid.TryParse(operationId, out var requestOperationGuid))
            {
                return new Result<string>(isSucceeded: false, message: $"Invalid {nameof(operationId)} has been provided");
            }

            var updatedOperation = new DepositOperation
            {
                Key = requestOperationGuid,
                Amount = request.Amount,
                Comment = request.Comment,
                CategoryId = Guid.Parse(request.CategoryId),
                ContractorId = Guid.Parse(request.ContractorId)
            };

            var elementForReplaceIndex = MockStore.DepositOperations.FindIndex(p => p.Key == requestOperationGuid);

            if (elementForReplaceIndex == -1)
            {
                return new Result<string>(isSucceeded: false, message: $"A operation with guid: '{nameof(operationId)}' hasn't been found");
            }

            MockStore.DepositOperations[elementForReplaceIndex] = updatedOperation;

            return new Result<string>(operationId);
        }
    }
}
