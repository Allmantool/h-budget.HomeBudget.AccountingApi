using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediatR;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Components.Contractors.Clients.Interfaces;
using HomeBudget.Components.Operations.Clients.Interfaces;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services
{
    internal class PaymentOperationsService(
        IPaymentAccountDocumentClient paymentAccountDocumentClient,
        ICategoryDocumentsClient categoryDocumentsClient,
        IContractorDocumentsClient contractorDocumentsClient,
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

            var referencesValidationResult = await ValidatePaymentReferencesAsync(payload.CategoryId, payload.ContractorId);
            if (!referencesValidationResult.IsSucceeded)
            {
                return Result<Guid>.Failure(referencesValidationResult.StatusMessage);
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

            var matches = documents
                .Where(d => d.Payload.Record.PaymentAccountId == paymentAccountId && d.Payload.Record.Key == operationId)
                .ToList();

            if (matches.Count == 0)
            {
                return Result<Guid>.Failure($"The operation '{operationId}' doesn't exist");
            }

            if (matches.Count > 1)
            {
                return Result<Guid>.Failure(
                    $"Duplicate payment operations were found for account '{paymentAccountId}' and operation '{operationId}'.");
            }

            var operationForDelete = matches[0];

            return await mediator.Send(
                new RemovePaymentOperationCommand(operationForDelete.Payload.Record),
                token);
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
                return Result<Guid>.Failure($"The payment account '{paymentAccountId}' hasn't been found");
            }

            var currentOperationStateDocument = await paymentsHistoryDocumentsClient.GetByIdAsync(paymentAccountId, operationId);

            if (currentOperationStateDocument is null)
            {
                return Result<Guid>.Failure($"The payment operation '{operationId}' for account '{paymentAccountId}' hasn't been found");
            }

            var currentOperaiton = currentOperationStateDocument.Payload;
            var paymentRecord = currentOperaiton.Record;
            var referencesValidationResult = await ValidatePaymentReferencesAsync(payload.CategoryId, payload.ContractorId);
            if (!referencesValidationResult.IsSucceeded)
            {
                return Result<Guid>.Failure(referencesValidationResult.StatusMessage);
            }

            var categoryIdResult = ResolveReferenceForUpdate(payload.CategoryId, paymentRecord.CategoryId, nameof(payload.CategoryId));
            if (!categoryIdResult.IsSucceeded)
            {
                return Result<Guid>.Failure(categoryIdResult.StatusMessage);
            }

            var contractorIdResult = ResolveReferenceForUpdate(payload.ContractorId, paymentRecord.ContractorId, nameof(payload.ContractorId));
            if (!contractorIdResult.IsSucceeded)
            {
                return Result<Guid>.Failure(contractorIdResult.StatusMessage);
            }

            var operationForUpdate = new FinancialTransaction
            {
                PaymentAccountId = paymentAccountId,
                Key = operationId,
                Amount = payload.Amount,
                Comment = payload.Comment,
                CategoryId = categoryIdResult.Payload,
                ContractorId = contractorIdResult.Payload,
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

        private async Task<Result<bool>> ValidatePaymentReferencesAsync(string categoryId, string contractorId)
        {
            var categoryIdResult = ParseOptionalReferenceId(categoryId, nameof(categoryId));
            if (!categoryIdResult.IsSucceeded)
            {
                return Result<bool>.Failure(categoryIdResult.StatusMessage);
            }

            var categoryGuid = categoryIdResult.Payload;
            if (categoryGuid != Guid.Empty)
            {
                var categoryResult = await categoryDocumentsClient.GetByIdAsync(categoryGuid);
                if (!categoryResult.IsSucceeded || categoryResult.Payload == null)
                {
                    return Result<bool>.Failure($"The category '{categoryGuid}' hasn't been found");
                }
            }

            var contractorIdResult = ParseOptionalReferenceId(contractorId, nameof(contractorId));
            if (!contractorIdResult.IsSucceeded)
            {
                return Result<bool>.Failure(contractorIdResult.StatusMessage);
            }

            var contractorGuid = contractorIdResult.Payload;
            if (contractorGuid != Guid.Empty)
            {
                var contractorResult = await contractorDocumentsClient.GetByIdAsync(contractorGuid);
                if (!contractorResult.IsSucceeded || contractorResult.Payload == null)
                {
                    return Result<bool>.Failure($"The contractor '{contractorGuid}' hasn't been found");
                }
            }

            return Result<bool>.Succeeded(true);
        }

        private static Result<Guid> ResolveReferenceForUpdate(string referenceId, Guid fallbackReferenceId, string referenceName)
        {
            if (string.IsNullOrWhiteSpace(referenceId))
            {
                return Result<Guid>.Succeeded(fallbackReferenceId);
            }

            return ParseOptionalReferenceId(referenceId, referenceName);
        }

        private static Result<Guid> ParseOptionalReferenceId(string referenceId, string referenceName)
        {
            if (string.IsNullOrWhiteSpace(referenceId))
            {
                return Result<Guid>.Succeeded(Guid.Empty);
            }

            return Guid.TryParse(referenceId, out var parsedReferenceId)
                ? Result<Guid>.Succeeded(parsedReferenceId)
                : Result<Guid>.Failure($"Invalid payment reference '{referenceName}' has been provided: '{referenceId}'");
        }
    }
}
