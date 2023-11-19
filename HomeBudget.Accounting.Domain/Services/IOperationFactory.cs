using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Services
{
    public interface IOperationFactory
    {
        PaymentOperation Create(
            decimal amount,
            string comment,
            string categoryId,
            string contractorId,
            string paymentAccountId);
    }
}
