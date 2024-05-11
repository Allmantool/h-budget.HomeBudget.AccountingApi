using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Factories
{
    public interface IPaymentAccountFactory
    {
        PaymentAccount Create(
            string agent,
            decimal balance,
            string currency,
            string description,
            AccountTypes accountType);
    }
}
