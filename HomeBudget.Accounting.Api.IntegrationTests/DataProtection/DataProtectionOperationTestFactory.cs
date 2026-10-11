using System;

using HomeBudget.Accounting.Infrastructure.DataProtection;
using HomeBudget.Accounting.Infrastructure.Providers.Interfaces;

namespace HomeBudget.Accounting.Api.IntegrationTests.DataProtection
{
    internal static class DataProtectionOperationTestFactory
    {
        private const string Fingerprint =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        public static DataProtectionOperationRequest CreateRequest(DateTime? createdUtc = null)
        {
            var operationId = Guid.NewGuid();
            return new DataProtectionOperationRequest
            {
                OperationId = operationId,
                OperationKind = "ledger-reset",
                ScopeKind = "ledger",
                ScopeKey = $"integration:ledger:{operationId:N}",
                EnvironmentId = "integration",
                LedgerId = "default",
                GenerationId = "legacy",
                PlanFingerprint = Fingerprint,
                ActorId = "integration-test-actor",
                CreatedUtc = createdUtc ?? DateTime.UtcNow
            };
        }

        public static DataProtectionOperationProcessor CreateProcessor(
            IDataProtectionOperationStore store,
            IDataProtectionOperationExecutor executor)
        {
            return new DataProtectionOperationProcessor(store, executor, new UtcDateTimeProvider());
        }

        private sealed class UtcDateTimeProvider : IDateTimeProvider
        {
            public DateTime GetNowUtc() => DateTime.UtcNow;
        }
    }
}
