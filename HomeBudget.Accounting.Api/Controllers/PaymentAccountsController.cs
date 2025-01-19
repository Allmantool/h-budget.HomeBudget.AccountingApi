using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route(Endpoints.PaymentAccounts, Name = Endpoints.PaymentAccounts)]
    public class PaymentAccountsController(
        Channel<SubscriptionTopic> topicsChannel,
        IMapper mapper,
        IPaymentAccountDocumentClient paymentAccountDocumentClient,
        IPaymentAccountFactory paymentAccountFactory) : ControllerBase
    {
        [HttpGet]
        public async Task<Result<IReadOnlyCollection<PaymentAccountResponse>>> GetAsync()
        {
            var documentsResult = await paymentAccountDocumentClient.GetAsync();

            if (!documentsResult.IsSucceeded)
            {
                return Result<IReadOnlyCollection<PaymentAccountResponse>>.Failure();
            }

            var paymentAccounts = documentsResult.Payload
                .Select(d => d.Payload)
                .OrderBy(c => c.Type)
                .ThenBy(c => c.Description)
                .ThenBy(op => op.OperationUnixTime)
                .ToList();

            return Result<IReadOnlyCollection<PaymentAccountResponse>>
                .Succeeded(mapper.Map<IReadOnlyCollection<PaymentAccountResponse>>(paymentAccounts));
        }

        [HttpGet("byId/{paymentAccountId}")]
        public async Task<Result<PaymentAccountResponse>> GetByIdAsync(string paymentAccountId)
        {
            if (!Guid.TryParse(paymentAccountId, out _))
            {
                return Result<PaymentAccountResponse>.Failure($"Invalid {nameof(paymentAccountId)} has been provided");
            }

            var documentResult = await paymentAccountDocumentClient.GetByIdAsync(paymentAccountId);

            if (!documentResult.IsSucceeded || documentResult.Payload == null)
            {
                return Result<PaymentAccountResponse>.Failure($"The payment account with '{paymentAccountId}' hasn't been found");
            }

            var document = documentResult.Payload;

            return Result<PaymentAccountResponse>.Succeeded(mapper.Map<PaymentAccountResponse>(document.Payload));
        }

        [HttpPost]
        public async Task<Result<Guid>> CreateNewAsync(
            [FromBody] CreatePaymentAccountRequest request,
            CancellationToken cancellationToken = default)
        {
            var newPaymentAccount = paymentAccountFactory.Create(
                request.Agent,
                request.InitialBalance,
                request.Currency,
                request.Description,
                BaseEnumeration.FromValue<AccountTypes>(request.AccountType));

            var saveResult = await paymentAccountDocumentClient.InsertOneAsync(newPaymentAccount);

            var topic = new SubscriptionTopic
            {
                Title = $"payment-account-{newPaymentAccount.Key}",
                ConsumerType = ConsumerTypes.PaymentOperations
            };

            await topicsChannel.Writer.WriteAsync(topic, cancellationToken);

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