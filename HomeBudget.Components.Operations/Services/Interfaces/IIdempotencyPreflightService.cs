using System;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Accounting.Infrastructure.DataProtection;

namespace HomeBudget.Components.Operations.Services.Interfaces
{
    public interface IIdempotencyPreflightService
    {
        Task<IdempotencyPreflight> GetIdempotencyPreflightAsync(
            Guid paymentAccountId,
            string commandType,
            string requestFingerprint,
            string idempotencyKey);

        Task<IdempotencyPreflight> GetIdempotencyPreflightAsync(
            Guid paymentAccountId,
            string commandType,
            string requestFingerprint,
            string idempotencyKey,
            LedgerGenerationScope expectedGeneration);
    }
}
