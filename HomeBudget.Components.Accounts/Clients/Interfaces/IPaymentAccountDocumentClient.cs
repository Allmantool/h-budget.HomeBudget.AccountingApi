using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;

namespace HomeBudget.Components.Accounts.Clients.Interfaces
{
    public interface IPaymentAccountDocumentClient : IDocumentClient
    {
        Task<Result<IReadOnlyCollection<PaymentAccountDocument>>> GetAsync();

        Task<Result<PaymentAccountDocument>> GetByIdAsync(string paymentAccountId);

        Task<Result<Guid>> InsertOneAsync(PaymentAccount payload);

        Task<Result<Guid>> RemoveAsync(string paymentAccountId);
        Task<Result<Guid>> UpdateAsync(string requestPaymentAccountGuid, PaymentAccount paymentAccountForUpdate);
    }
}
