using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts;
using HomeBudget.Components.Operations;

namespace HomeBudget.Accounting.Api.Controllers
{
    [Route(Endpoints.PaymentsHistoryByPaymentAccountId, Name = Endpoints.PaymentsHistory)]
    [ApiController]
    public class PaymentsHistoryController : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<PaymentOperationHistoryRecord>> GetHistoryPaymentOperations(string paymentAccountId)
        {
            var paymentAccountOperations = MockOperationsHistoryStore.RecordsForAccount(Guid.Parse(paymentAccountId))
                .OrderBy(op => op.Record.OperationDay)
                .ThenBy(op => op.Record.OperationUnixTime)
                .ToList();

            return new Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>(paymentAccountOperations);
        }

        [HttpGet("byId/{operationId}")]
        public Result<PaymentOperationHistoryRecord> GetOperationById(string paymentAccountId, string operationId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetAccountGuid) || MockAccountsStore.Records.All(pa => pa.Key.CompareTo(targetAccountGuid) != 0))
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

            var operationById = MockOperationsHistoryStore.RecordsForAccount(targetAccountGuid)
                .Where(op => op.Record.PaymentAccountId.CompareTo(targetAccountGuid) == 0)
                .SingleOrDefault(rc => rc.Record.Key.CompareTo(targetOperationGuid) == 0);

            return operationById == null
                ? new Result<PaymentOperationHistoryRecord>(isSucceeded: false, message: $"The operation with '{operationId}' hasn't been found")
                : new Result<PaymentOperationHistoryRecord>(payload: operationById);
        }
    }
}
