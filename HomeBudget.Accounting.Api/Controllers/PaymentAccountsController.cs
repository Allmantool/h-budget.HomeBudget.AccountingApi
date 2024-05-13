﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route(Endpoints.PaymentAccounts, Name = Endpoints.PaymentAccounts)]
    public class PaymentAccountsController(
        IPaymentAccountDocumentClient paymentAccountDocumentClient,
        IPaymentAccountFactory paymentAccountFactory) : ControllerBase
    {
        [HttpGet]
        public async Task<Result<IReadOnlyCollection<PaymentAccount>>> GetAsync()
        {
            var documentsResult = await paymentAccountDocumentClient.GetAsync();

            if (!documentsResult.IsSucceeded)
            {
                return Result<IReadOnlyCollection<PaymentAccount>>.Failure();
            }

            var paymentAccounts = documentsResult.Payload
                .Select(d => d.Payload)
                .OrderBy(c => c.Type)
                .ThenBy(c => c.Description)
                .ThenBy(op => op.OperationUnixTime)
                .ToList();

            return Result<IReadOnlyCollection<PaymentAccount>>.Succeeded(paymentAccounts);
        }

        [HttpGet("byId/{paymentAccountId}")]
        public async Task<Result<PaymentAccount>> GetByIdAsync(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out _))
            {
                return Result<PaymentAccount>.Failure($"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var documentResult = await paymentAccountDocumentClient.GetByIdAsync(paymentAccountId);

            if (!documentResult.IsSucceeded || documentResult.Payload == null)
            {
                return Result<PaymentAccount>.Failure($"The payment account with '{paymentAccountId}' hasn't been found");
            }

            var document = documentResult.Payload;

            return Result<PaymentAccount>.Succeeded(document.Payload);
        }

        [HttpPost]
        public async Task<Result<Guid>> CreateNewAsync([FromBody] CreatePaymentAccountRequest request)
        {
            var newPaymentAccount = paymentAccountFactory.Create(
                request.Agent,
                request.InitialBalance,
                request.Currency,
                request.Description,
                BaseEnumeration.FromValue<AccountTypes>(request.AccountType));

            var saveResult = await paymentAccountDocumentClient.InsertOneAsync(newPaymentAccount);

            return Result<Guid>.Succeeded(saveResult.Payload);
        }

        [HttpDelete("{paymentAccountId}")]
        public async Task<Result<Guid>> DeleteByIdAsync(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out _))
            {
                return Result<Guid>.Failure($"Invalid '{nameof(paymentAccountId)}' has been provided");
            }

            var deleteResult = await paymentAccountDocumentClient.RemoveAsync(paymentAccountId);

            return Result<Guid>.Succeeded(deleteResult.Payload);
        }

        [HttpPatch("{paymentAccountId}")]
        public async Task<Result<Guid>> UpdateAsync(string paymentAccountId, [FromBody] UpdatePaymentAccountRequest request)
        {
            if (!Guid.TryParse(paymentAccountId, out _))
            {
                return Result<Guid>.Failure($"Invalid '{nameof(paymentAccountId)}' has been provided");
            }

            var paymentAccountForUpdate = new PaymentAccount
            {
                Agent = request.Agent,
                Balance = request.Balance,
                Currency = request.Currency,
                Description = request.Description,
                Type = BaseEnumeration.FromValue<AccountTypes>(request.AccountType)
            };

            var updateResult = await paymentAccountDocumentClient.UpdateAsync(paymentAccountId, paymentAccountForUpdate);

            return Result<Guid>.Succeeded(updateResult.Payload);
        }
    }
}