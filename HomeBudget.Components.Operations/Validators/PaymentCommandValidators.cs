using System;
using System.Collections.Generic;
using System.Linq;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Commands.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Components.Operations.Validators
{
    internal abstract class PaymentOperationCommandValidatorBase
    {
        protected static IReadOnlyCollection<string> ValidatePaymentOperation(
            FinancialTransaction operation,
            string operationName)
        {
            var failures = new List<string>();

            if (operation == null)
            {
                return [$"{operationName} is required"];
            }

            if (operation.PaymentAccountId == Guid.Empty)
            {
                failures.Add("Payment account id is required");
            }

            if (operation.Key == Guid.Empty)
            {
                failures.Add("Payment operation id is required");
            }

            if (operation.Amount == 0m)
            {
                failures.Add("Amount must not be zero");
            }

            if (operation.OperationDay == default)
            {
                failures.Add("Operation date is required");
            }

            if (operation.TransactionType == null)
            {
                failures.Add("Transaction type is required");
            }

            return failures;
        }

        protected static IReadOnlyCollection<string> ValidateTransferOperations(
            Guid transferId,
            IReadOnlyCollection<FinancialTransaction> operations)
        {
            var failures = new List<string>();

            if (transferId == Guid.Empty)
            {
                failures.Add("Transfer operation id is required");
            }

            if (operations == null || operations.Count != 2)
            {
                failures.Add("Transfer requires sender and recipient operations");
                return failures;
            }

            foreach (var operation in operations)
            {
                failures.AddRange(ValidatePaymentOperation(operation, "Transfer operation"));

                if (operation?.TransactionType != TransactionTypes.Transfer)
                {
                    failures.Add("Transfer operation transaction type is invalid");
                }
            }

            if (operations.Any(operation => operation == null))
            {
                return failures;
            }

            if (operations.Select(operation => operation.PaymentAccountId).Distinct().Count() != operations.Count)
            {
                failures.Add("Sender and recipient accounts must be different");
            }

            return failures;
        }
    }

    internal sealed class AddPaymentOperationCommandValidator
        : PaymentOperationCommandValidatorBase,
            IRequestValidator<AddPaymentOperationCommand>
    {
        public IReadOnlyCollection<string> Validate(AddPaymentOperationCommand request)
        {
            return ValidatePaymentOperation(request.OperationForAdd, "Payment operation");
        }
    }

    internal sealed class UpdatePaymentOperationCommandValidator
        : PaymentOperationCommandValidatorBase,
            IRequestValidator<UpdatePaymentOperationCommand>
    {
        public IReadOnlyCollection<string> Validate(UpdatePaymentOperationCommand request)
        {
            return ValidatePaymentOperation(request.OperationForUpdate, "Payment operation");
        }
    }

    internal sealed class RemovePaymentOperationCommandValidator
        : PaymentOperationCommandValidatorBase,
            IRequestValidator<RemovePaymentOperationCommand>
    {
        public IReadOnlyCollection<string> Validate(RemovePaymentOperationCommand request)
        {
            return ValidatePaymentOperation(request.OperationForDelete, "Payment operation");
        }
    }

    internal sealed class ApplyTransferCommandValidator
        : PaymentOperationCommandValidatorBase,
            IRequestValidator<ApplyTransferCommand>
    {
        public IReadOnlyCollection<string> Validate(ApplyTransferCommand request)
        {
            return ValidateTransferOperations(request.Key, request.PaymentOperations);
        }
    }

    internal sealed class UpdateTransferCommandValidator
        : PaymentOperationCommandValidatorBase,
            IRequestValidator<UpdateTransferCommand>
    {
        public IReadOnlyCollection<string> Validate(UpdateTransferCommand request)
        {
            return ValidateTransferOperations(request.Key, request.PaymentOperations);
        }
    }

    internal sealed class RemoveTransferCommandValidator
        : PaymentOperationCommandValidatorBase,
            IRequestValidator<RemoveTransferCommand>
    {
        public IReadOnlyCollection<string> Validate(RemoveTransferCommand request)
        {
            return ValidateTransferOperations(request.Key, request.PaymentOperations);
        }
    }
}
