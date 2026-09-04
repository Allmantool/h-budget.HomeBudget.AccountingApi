using System;
using System.Threading.Tasks;

using HomeBudget.Components.Operations.Models;
using HomeBudget.Components.Operations.Services.Interfaces;

namespace HomeBudget.Components.Operations.Services
{
    internal class IdempotencyPreflightService(IOutboxPaymentStatusService outboxPaymentStatusService) : IIdempotencyPreflightService
    {
        public async Task<IdempotencyPreflight> GetIdempotencyPreflightAsync(
            Guid paymentAccountId,
            string commandType,
            string requestFingerprint,
            string idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return new IdempotencyPreflight();
            }

            if (idempotencyKey.Length > 200)
            {
                return new IdempotencyPreflight { IsConflict = true };
            }

            var keyHash = PaymentCommandFingerprint.HashIdempotencyKey(idempotencyKey);
            var existingCommand = await outboxPaymentStatusService.GetCommandByIdempotencyKeyAsync(paymentAccountId, keyHash);
            if (existingCommand is not null)
            {
                return new IdempotencyPreflight
                {
                    ExistingCommand = existingCommand,
                    IsConflict = !string.Equals(existingCommand.RequestFingerprint, requestFingerprint, StringComparison.Ordinal)
                };
            }

            return new IdempotencyPreflight
            {
                Context = new PaymentCommandContext
                {
                    IdempotencyKeyHash = keyHash,
                    RequestFingerprint = requestFingerprint,
                    CommandType = commandType
                }
            };
        }
    }
}
