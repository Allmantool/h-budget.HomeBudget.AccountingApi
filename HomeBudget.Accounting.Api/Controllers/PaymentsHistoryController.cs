using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Clients.Interfaces;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentsHistoryByPaymentAccountId, Name = Endpoints.PaymentsHistory)]
    [ApiController]
    public class PaymentsHistoryController(IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient) : ControllerBase
    {
        [HttpGet]
        public async Task<Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>> GetHistoryPaymentOperationsAsync(string paymentAccountId)
        {
            var documents = await paymentsHistoryDocumentsClient.GetAsync(Guid.Parse(paymentAccountId));

            var paymentAccountOperations = documents
                .Select(d => d.Payload)
                .OrderBy(op => op.Record.OperationDay)
                .ThenBy(op => op.Record.OperationUnixTime)
                .ToList();

            return new Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>(paymentAccountOperations);
        }

        [HttpGet("byId/{operationId}")]
        public async Task<Result<PaymentOperationHistoryRecord>> GetOperationByIdAsync(string paymentAccountId, string operationId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid))
            {
                return new Result<PaymentOperationHistoryRecord>(
                    isSucceeded: false,
                    message: $"Invalid payment account '{nameof(targetAccountGuid)}' has been provided");
            }

            if (!Guid.TryParse(operationId, out var targetOperationGuid))
            {
                return new Result<PaymentOperationHistoryRecord>(
                    isSucceeded: false,
                    message: $"Invalid payment operation '{nameof(targetOperationGuid)}' has been provided");
            }

            var document = await paymentsHistoryDocumentsClient.GetByIdAsync(targetAccountGuid, targetAccountGuid);

            var operationById = document.Payload;

            return operationById == null
                ? new Result<PaymentOperationHistoryRecord>(isSucceeded: false, message: $"The operation with '{operationId}' hasn't been found")
                : new Result<PaymentOperationHistoryRecord>(payload: operationById);
        }
    }
}
