using System;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Factories
{
    public interface IOperationFactory
    {
        Result<PaymentOperation> CreatePaymentOperation(
            Guid paymentAccountId,
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            DateOnly operationDay);

        Result<PaymentOperation> CreateTransferOperation(
            Guid paymentAccountId,
            decimal amount,
            DateOnly operationDay);
    }
}
