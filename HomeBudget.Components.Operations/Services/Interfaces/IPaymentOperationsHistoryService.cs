using System;
using System.Threading.Tasks;

using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentOperationsHistoryService
    {
        Task<Result<decimal>> SyncHistoryAsync(Guid paymentAccountId);
    }
}
