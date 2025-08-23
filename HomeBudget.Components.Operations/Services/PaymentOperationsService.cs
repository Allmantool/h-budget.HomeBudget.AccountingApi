using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsService(
        IPaymentAccountDocumentClient paymentAccountDocumentClient,
        IPaymentsHistoryDocumentsClient paymentsHistoryDocumentsClient,
        ISender mediator,
        IFinancialTransactionFactory financialTransactionFactory)
        : IPaymentOperationsService
    {
        public async Task<Result<Guid>> CreateAsync(Guid paymentAccountId, PaymentOperationPayload payload, CancellationToken token)
        {
            var isPaymentAccountExist = await IsPaymentAccountExistAsync(paymentAccountId.ToString());

            if (!isPaymentAccountExist)
            {
                return Result<Guid>.Failure($"The payment account '{nameof(paymentAccountId)}' hasn't been found");
            }

            var operationForAddResult = financialTransactionFactory.CreatePayment(
                paymentAccountId,
                payload.ScopeOperationId,
                payload.Amount,
                payload.Comment,
                payload.CategoryId,
                payload.ContractorId,
                payload.OperationDate);

            if (!operationForAddResult.IsSucceeded)
            {
                return Result<Guid>.Failure($"An 'operation' hasn't been created successfully. Details: '{operationForAddResult.StatusMessage}'");
            }

            return await mediator.Send(new AddPaymentOperationCommand(operationForAddResult.Payload), token);
        }

        public async Task<Result<Guid>> RemoveAsync(
            Guid paymentAccountId,
            Guid operationId,
            CancellationToken token)
        {
            var isPaymentAccountExist = await IsPaymentAccountExistAsync(paymentAccountId.ToString());

            if (!isPaymentAccountExist)
            {
                return Result<Guid>.Failure($"The payment account '{nameof(paymentAccountId)}' hasn't been found");
            }

            var documents = await paymentsHistoryDocumentsClient.GetAsync(paymentAccountId);

            var operationForDelete = documents
                .Where(op => op.Payload.Record.PaymentAccountId.CompareTo(paymentAccountId) == 0)
                .SingleOrDefault(p => p.Payload.Record.Key.CompareTo(operationId) == 0);

            return operationForDelete == null
                ? Result<Guid>.Failure($"The operation '{operationId}' doesn't exist")
                : await mediator.Send(new RemovePaymentOperationCommand(operationForDelete.Payload.Record), token);
        }

        public async Task<Result<Guid>> UpdateAsync(
            Guid paymentAccountId,
            Guid operationId,
            PaymentOperationPayload payload,
            CancellationToken token)
        {
            var isPaymentAccountExist = await IsPaymentAccountExistAsync(paymentAccountId.ToString());

            if (!isPaymentAccountExist)
            {
                return Result<Guid>.Failure($"The payment account '{nameof(paymentAccountId)}' hasn't been found");
            }

            var operationForUpdate = new FinancialTransaction
            {
                PaymentAccountId = paymentAccountId,
                Key = operationId,
                Amount = payload.Amount,
                Comment = payload.Comment,
                CategoryId = Guid.Parse(payload.CategoryId),
                ContractorId = Guid.Parse(payload.ContractorId),
                OperationDay = payload.OperationDate,
                TransactionType = TransactionTypes.Payment
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
