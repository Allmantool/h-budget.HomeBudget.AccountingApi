using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentOperationsHistoryService
    {
        Task<Result<decimal>> SyncHistoryAsync(
            string financialPeriodIdentifier,
            IEnumerable<PaymentOperationEvent> eventsForAccount,
            ProjectionCheckpoint checkpoint = null,
            CancellationToken cancellationToken = default,
            Guid? completionOwnerRunId = null);
    }
}
