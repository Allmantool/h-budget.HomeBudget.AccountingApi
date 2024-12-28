using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentOperationsHistoryService
    {
        Task<Result<decimal>> SyncHistoryAsync(string financialPeriodIdentifier, IEnumerable<PaymentOperationEvent> eventsForAccount);
    }
}
