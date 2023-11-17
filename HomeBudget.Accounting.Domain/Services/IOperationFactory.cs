using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Services
{
    public interface IOperationFactory
    {
        DepositOperation Create(
            decimal amount,
            string comment,
            string categoryId,
            string contractorId);
    }
}
