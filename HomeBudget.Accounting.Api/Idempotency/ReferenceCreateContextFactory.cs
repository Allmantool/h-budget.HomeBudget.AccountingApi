using System;

using HomeBudget.Accounting.Infrastructure.Clients;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Accounting.Api.Idempotency
{
    internal static class ReferenceCreateContextFactory
    {
        public static bool TryCreate(
            string idempotencyKey,
            string requestFingerprint,
            string sourceReference,
            out IdempotentDocumentWriteContext context)
        {
            context = null;
            if (string.IsNullOrWhiteSpace(idempotencyKey) ||
                idempotencyKey.Length > 200 ||
                string.IsNullOrWhiteSpace(sourceReference) ||
                sourceReference.Length > 500)
            {
                return false;
            }

            context = new IdempotentDocumentWriteContext(
                PaymentCommandFingerprint.HashIdempotencyKey(idempotencyKey),
                requestFingerprint,
                sourceReference.Trim());
            return true;
        }
    }
}
