using System;
using System.Threading;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IPaymentOperationsService
    {
        Task<Result<Guid>> CreateAsync(string paymentAccountId, PaymentOperationPayload payload, CancellationToken token);
    }
}
