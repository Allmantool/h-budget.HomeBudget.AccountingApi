﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Domain.Services;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.CQRS.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsService(
        IPaymentAccountDocumentClient paymentAccountDocumentClient,
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        ISender mediator,
        IOperationFactory operationFactory)
        : IPaymentOperationsService
    {
        public async Task<Result<Guid>> CreateAsync(Guid paymentAccountId, PaymentOperationPayload payload, CancellationToken token)
        {
            var isPaymentAccountExist = await IsPaymentAccountExistAsync(paymentAccountId.ToString());

            if (!isPaymentAccountExist)
            {
                return new Result<Guid>(
                    isSucceeded: false,
                    message: $"The payment account '{nameof(paymentAccountId)}' hasn't been found");
            }

            var operationForAddResult = operationFactory.Create(
                paymentAccountId,
                payload.Amount,
                payload.Comment,
                payload.CategoryId,
                payload.ContractorId,
                payload.OperationDate);

            if (!operationForAddResult.IsSucceeded)
            {
                return new Result<Guid>(isSucceeded: false, message: "'operation' hasn't been created successfully");
            }

            return await mediator.Send(new SavePaymentOperationCommand(operationForAddResult.Payload), token);
        }

        public async Task<Result<Guid>> RemoveAsync(Guid paymentAccountId, Guid operationId, CancellationToken token)
        {
            var isPaymentAccountExist = await IsPaymentAccountExistAsync(paymentAccountId.ToString());

            if (!isPaymentAccountExist)
            {
                return new Result<Guid>(
                    isSucceeded: false,
                    message: $"The payment account '{nameof(paymentAccountId)}' hasn't been found");
            }

            var documents = await paymentsHistoryDocumentsClient.GetAsync(paymentAccountId);

            var operationForDelete = documents
                .Where(op => op.Payload.Record.PaymentAccountId.CompareTo(paymentAccountId) == 0)
                .SingleOrDefault(p => p.Payload.Record.Key.CompareTo(operationId) == 0);

            return operationForDelete == null
                ? new Result<Guid>(isSucceeded: false, message: $"The operation '{operationId}' doesn't exist")
                : await mediator.Send(new RemovePaymentOperationCommand(operationForDelete.Payload.Record), token);
        }

        public async Task<Result<Guid>> UpdateAsync(Guid paymentAccountId, Guid operationId, PaymentOperationPayload payload, CancellationToken token)
        {
            var isPaymentAccountExist = await IsPaymentAccountExistAsync(paymentAccountId.ToString());

            if (!isPaymentAccountExist)
            {
                return new Result<Guid>(
                    isSucceeded: false,
                    message: $"The payment account '{nameof(paymentAccountId)}' hasn't been found");
            }

            var operationForUpdate = new PaymentOperation
            {
                PaymentAccountId = paymentAccountId,
                Key = operationId,
                Amount = payload.Amount,
                Comment = payload.Comment,
                CategoryId = Guid.Parse(payload.CategoryId),
                ContractorId = Guid.Parse(payload.ContractorId),
                OperationDay = payload.OperationDate
            };

            return await mediator.Send(new UpdatePaymentOperationCommand(operationForUpdate), token);
        }

        private async Task<bool> IsPaymentAccountExistAsync(string paymentAccountId)
        {
            var isPaymentAccountExist = await paymentAccountDocumentClient.GetByIdAsync(paymentAccountId);

            return isPaymentAccountExist.IsSucceeded && isPaymentAccountExist.Payload != null;
        }
    }
}
