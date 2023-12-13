using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentOperationsHistoryService
    {
        Result<decimal> SyncHistory();
    }
}
