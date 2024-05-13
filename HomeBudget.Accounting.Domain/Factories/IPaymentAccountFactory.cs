using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Factories
{
    public interface IPaymentAccountFactory
    {
        PaymentAccount Create(
            string agent,
            decimal initialBalance,
            string currency,
            string description,
            AccountTypes accountType);
    }
}
