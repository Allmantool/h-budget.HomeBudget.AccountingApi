using System.Threading.Tasks;

using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Services.Interfaces;

namespace HomeBudget.Components.Accounts.Services
{
    internal class PaymentAccountService(IPaymentAccountDocumentClient paymentAccountDocumentClient)
        : IPaymentAccountService
    {
        public async Task<decimal> GetInitialBalanceAsync(string paymentAccountId)
        {
            var paymentAccountDocumentResult = await paymentAccountDocumentClient.GetByIdAsync(paymentAccountId);
            var document = paymentAccountDocumentResult.Payload;

            return document == null ? 0 : document.Payload.InitialBalance;
        }
    }
}
