using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Api.Models.PaymentAccount;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route("paymentAccounts")]
    public class PaymentAccountsController : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<PaymentAccount>> Get()
        {
            return new Result<IReadOnlyCollection<PaymentAccount>>(MockStore.PaymentAccounts);
        }

        [HttpGet("byId/{paymentAccountId}")]
        public Result<PaymentAccount> GetById(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetGuid))
            {
                return new Result<PaymentAccount>(isSucceeded: false, message: $"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var payload = MockStore.PaymentAccounts.SingleOrDefault(p => p.Key == targetGuid);

            return payload == null
                ? new Result<PaymentAccount>(isSucceeded: false, message: "Payment account with provided guid is not exist")
                : new Result<PaymentAccount>(payload);
        }

        [HttpPost]
        public Result<string> CreateNew([FromBody] CreatePaymentAccountRequest request)
        {
            var newPaymentAccount = new PaymentAccount
            {
                Key = Guid.NewGuid(),
                Agent = request.Agent,
                Balance = request.Balance,
                Currency = request.Currency,
                Description = request.Description,
                Type = request.AccountType
            };

            MockStore.PaymentAccounts.Add(newPaymentAccount);

            return new Result<string>(newPaymentAccount.Key.ToString());
        }

        [HttpDelete("{paymentAccountId}")]
        public Result<bool> DeleteById(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetGuid))
            {
                return new Result<bool>(isSucceeded: false, message: $"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var paymentAccountForDelete = MockStore.PaymentAccounts.FirstOrDefault(p => p.Key == targetGuid);
            var isRemoveSuccessful = MockStore.PaymentAccounts.Remove(paymentAccountForDelete);

            return new Result<bool>(payload: isRemoveSuccessful, isSucceeded: isRemoveSuccessful);
        }

        [HttpPatch("{paymentAccountId}")]
        public Result<string> Update(string paymentAccountId, [FromBody] UpdatePaymentAccountRequest request)
        {
            if (!Guid.TryParse(paymentAccountId, out var requestPaymentAccountGuid))
            {
                return new Result<string>(isSucceeded: false, message: $"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var updatedPaymentAccount = new PaymentAccount
            {
                Key = requestPaymentAccountGuid,
                Agent = request.Agent,
                Balance = request.Balance,
                Currency = request.Currency,
                Description = request.Description,
                Type = request.AccountType
            };

            var elementForReplaceIndex = MockStore.PaymentAccounts.FindIndex(p => p.Key == requestPaymentAccountGuid);

            if (elementForReplaceIndex == -1)
            {
                return new Result<string>(isSucceeded: false, message: $"A payment account with guid: '{nameof(paymentAccountId)}' hasn't been found");
            }

            MockStore.PaymentAccounts[elementForReplaceIndex] = updatedPaymentAccount;

            return new Result<string>(paymentAccountId);
        }
    }
}