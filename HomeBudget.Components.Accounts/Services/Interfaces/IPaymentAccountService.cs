using System.Threading.Tasks;

namespace HomeBudget.Components.Accounts.Services.Interfaces
{
    public interface IPaymentAccountService
    {
        Task<decimal> GetInitialBalanceAsync(string paymentAccountId);
    }
}
