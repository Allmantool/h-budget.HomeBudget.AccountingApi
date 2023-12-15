using System;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentOperationsService
    {
        Task<Result<Guid>> CreateAsync(Guid paymentAccountId, PaymentOperationPayload payload, CancellationToken token);
        Task<Result<Guid>> RemoveAsync(Guid paymentAccountId, Guid operationId, CancellationToken token);
        Task<Result<Guid>> UpdateAsync(Guid paymentAccountId, Guid operationId, PaymentOperationPayload payload, CancellationToken token);
    }
}
