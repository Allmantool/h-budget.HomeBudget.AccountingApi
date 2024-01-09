using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Services
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
