using System;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IIdempotencyPreflightService
    {
        Task<IdempotencyPreflight> GetIdempotencyPreflightAsync(
            Guid paymentAccountId,
            string commandType,
            string requestFingerprint,
            string idempotencyKey);
    }
}
