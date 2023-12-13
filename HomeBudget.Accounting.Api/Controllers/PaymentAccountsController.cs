using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route(Endpoints.PaymentAccounts, Name = Endpoints.PaymentAccounts)]
    public class PaymentAccountsController : ControllerBase
    {
        [HttpGet]
        public Result<IReadOnlyCollection<PaymentAccount>> Get()
        {
            return new Result<IReadOnlyCollection<PaymentAccount>>(MockAccountsStore.Records);
        }

        [HttpGet("byId/{paymentAccountId}")]
        public Result<PaymentAccount> GetById(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetGuid))
            {
                return new Result<PaymentAccount>(isSucceeded: false, message: $"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var payload = MockAccountsStore.Records.SingleOrDefault(p => p.Key == targetGuid);

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

            MockAccountsStore.Records.Add(newPaymentAccount);

            return new Result<string>(newPaymentAccount.Key.ToString());
        }

        [HttpDelete("{paymentAccountId}")]
        public Result<bool> DeleteById(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out var targetGuid))
            {
                return new Result<bool>(isSucceeded: false, message: $"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var paymentAccountForDelete = MockAccountsStore.Records.Find(p => p.Key == targetGuid);
            var isRemoveSuccessful = MockAccountsStore.Records.Remove(paymentAccountForDelete);

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

            var elementForReplaceIndex = MockAccountsStore.Records.FindIndex(p => p.Key == requestPaymentAccountGuid);

            if (elementForReplaceIndex == -1)
            {
                return new Result<string>(isSucceeded: false, message: $"A payment account with guid: '{nameof(paymentAccountId)}' hasn't been found");
            }

            MockAccountsStore.Records[elementForReplaceIndex] = updatedPaymentAccount;

            return new Result<string>(paymentAccountId);
        }
    }
}