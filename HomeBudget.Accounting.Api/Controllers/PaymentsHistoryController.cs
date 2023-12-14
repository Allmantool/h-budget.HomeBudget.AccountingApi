using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Domain.Models;
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
            var paymentAccountOperations = MockOperationsHistoryStore.Records
                .Where(op => op.Record.PaymentAccountId.CompareTo(Guid.Parse(paymentAccountId)) == 0)
                .OrderBy(op => op.Record.OperationDay)
                .ToList();

            return new Result<IReadOnlyCollection<PaymentOperationHistoryRecord>>(paymentAccountOperations);
        }
    }
}
