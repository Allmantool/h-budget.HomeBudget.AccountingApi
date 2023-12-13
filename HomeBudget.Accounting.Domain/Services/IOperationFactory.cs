using System;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Services
{
    public interface IOperationFactory
    {
        PaymentOperation Create(
            string paymentAccountId,
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            DateOnly operationDay);
    }
}
