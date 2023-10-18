using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget_Accounting_Api.Models;

namespace HomeBudget_Accounting_Api.Controllers
{
    [ApiController]
    [Route("paymentAccounts")]
    public class PaymentAccountsController : ControllerBase
    {
        private readonly ILogger<PaymentAccountsController> _logger;

        public PaymentAccountsController(ILogger<PaymentAccountsController> logger)
        {
            _logger = logger;
        }

        [HttpGet("GetPaymentAccounts")]
        public Result<IEnumerable<PaymentAccount>> Get()
        {
            return new Result<IEnumerable<PaymentAccount>>(MockStore.PaymentAccounts);
        }

        [HttpGet("GetPaymentAccount")]
        public Result<PaymentAccount> GetById(string paymentAccountId)
        {
            var targetGuid = Guid.Parse(paymentAccountId);
            var payload = MockStore.PaymentAccounts.SingleOrDefault(p => p.Id == targetGuid);

            return payload == null
                ? new Result<PaymentAccount>(isSucceeded: false, message: "Payment account with provided guid is not exist")
                : new Result<PaymentAccount>(payload);
        }

        [HttpPost("MakePaymentAccount")]
        public Result<Guid> CreateNew(CreatePaymentAccountRequest request)
        {
            var newPaymentAccount = new PaymentAccount
            {
                Id = Guid.NewGuid(),
                Agent = request.Agent,
                Balance = request.Balance,
                Currency = request.Currency,
                Description = request.Description,
                Type = request.AccountType
            };

            MockStore.PaymentAccounts.Add(newPaymentAccount);

            return new Result<Guid>(newPaymentAccount.Id);
        }

        [HttpDelete("RemovePaymentAccount")]
        public Result<bool> DeleteById(string paymentAccountId)
        {
            var targetGuid = Guid.Parse(paymentAccountId);
            var paymentAccountForDelete = MockStore.PaymentAccounts.FirstOrDefault(p => p.Id == targetGuid);

            MockStore.PaymentAccounts.Remove(paymentAccountForDelete);

            return new Result<bool>(isSucceeded: MockStore.PaymentAccounts.Remove(paymentAccountForDelete));
        }

        [HttpPatch("UpdatePaymentAccount")]
        public Result<bool> Update(UpdatePaymentAccountRequest request)
        {
            if (!Guid.TryParse(request.Id, out var requestPaymentAccountGuid))
            {
                return new Result<bool>();
            }

            var updatedPaymentAccount = new PaymentAccount
            {
                Id = requestPaymentAccountGuid,
                Agent = request.Agent,
                Balance = request.Balance,
                Currency = request.Currency,
                Description = request.Description,
                Type = request.AccountType
            };

            var elementForReplaceIndex = MockStore.PaymentAccounts.FindIndex(p => p.Id == requestPaymentAccountGuid);

            if (elementForReplaceIndex == -1)
            {
                return new Result<bool>();
            }

            MockStore.PaymentAccounts[elementForReplaceIndex] = updatedPaymentAccount;

            return new Result<bool>(true);
        }
    }
}