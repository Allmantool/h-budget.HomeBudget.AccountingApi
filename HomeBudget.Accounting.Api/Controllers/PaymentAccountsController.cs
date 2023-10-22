using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Api.Models;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Api.Controllers
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

        [HttpGet("getPaymentAccounts")]
        public Result<IReadOnlyCollection<PaymentAccount>> Get()
        {
            return new Result<IReadOnlyCollection<PaymentAccount>>(MockStore.PaymentAccounts);
        }

        [HttpGet("getPaymentAccountById/{paymentAccountId}")]
        public Result<PaymentAccount> GetById(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetGuid))
            {
                return new Result<PaymentAccount>(isSucceeded: false, message: $"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var payload = MockStore.PaymentAccounts.SingleOrDefault(p => p.Id == targetGuid);

            return payload == null
                ? new Result<PaymentAccount>(isSucceeded: false, message: "Payment account with provided guid is not exist")
                : new Result<PaymentAccount>(payload);
        }

        [HttpPost("makePaymentAccount")]
        public Result<string> CreateNew([FromBody] CreatePaymentAccountRequest request)
        {
            var newPaymentAccount = new PaymentAccount
            {
                Id = Guid.NewGuid(),
                Agent = request.Agent,
                Balance = request.Balance,
                Currency = request.CurrencyAbbreviation,
                Description = request.Description,
                Type = request.AccountType
            };

            MockStore.PaymentAccounts.Add(newPaymentAccount);

            return new Result<string>(newPaymentAccount.Id.ToString());
        }

        [HttpDelete("removePaymentAccount/{paymentAccountId}")]
        public Result<bool> DeleteById(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetGuid))
            {
                return new Result<bool>(isSucceeded: false, message: $"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var paymentAccountForDelete = MockStore.PaymentAccounts.FirstOrDefault(p => p.Id == targetGuid);
            var isRemoveSuccessful = MockStore.PaymentAccounts.Remove(paymentAccountForDelete);

            return new Result<bool>(payload: isRemoveSuccessful, isSucceeded: isRemoveSuccessful);
        }

        [HttpPatch("updatePaymentAccount/{paymentAccountId}")]
        public Result<string> Update(string paymentAccountId, [FromBody] UpdatePaymentAccountRequest request)
        {
            if (!Guid.TryParse(paymentAccountId, out var requestPaymentAccountGuid))
            {
                return new Result<string>(isSucceeded: false, message: $"Invalid {nameof(paymentAccountId)} has been provided");
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
                return new Result<string>(isSucceeded: false, message: $"A payment account with guid: '{nameof(paymentAccountId)}' hasn't been found");
            }

            MockStore.PaymentAccounts[elementForReplaceIndex] = updatedPaymentAccount;

            return new Result<string>(paymentAccountId);
        }
    }
}