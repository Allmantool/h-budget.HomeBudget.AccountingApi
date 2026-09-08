using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentOperationHistoryService
    {
        Task<IReadOnlyCollection<PaymentOperationHistoryRecord>> GetHistoryWithRunningBalancesAsync(Guid paymentAccountId);
    }
}
