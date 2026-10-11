using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Accounting.Workers.OperationsConsumer.Models
{
    internal static class ProjectionRebuildInventoryFingerprint
    {
        public static string Compute(ProjectionRebuildPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            var canonical = new StringBuilder();
            Append(canonical, plan.Target);
            Append(canonical, plan.MongoEndpoint);
            Append(canonical, plan.MongoDatabase);
            Append(canonical, plan.EventStoreEndpoint);
            Append(canonical, plan.EnvironmentId);
            Append(canonical, plan.LedgerId);
            Append(canonical, plan.LedgerGenerationId);
            Append(canonical, plan.ExternalSideEffectsSuppressed ? "1" : "0");
            foreach (var item in plan.Items
                .OrderBy(static item => item.StreamId, StringComparer.Ordinal)
                .ThenBy(static item => item.PaymentAccountId))
            {
                Append(canonical, item.PaymentAccountId.ToString("D"));
                Append(canonical, item.FinancialPeriodIdentifier);
                Append(canonical, item.StreamId);
                Append(canonical, item.ExpectedRevision.ToString(CultureInfo.InvariantCulture));
                Append(canonical, item.ExpectedEventCount.ToString(CultureInfo.InvariantCulture));
                Append(canonical, item.PublishedRevision?.ToString(CultureInfo.InvariantCulture));
            }

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        }

        private static void Append(StringBuilder canonical, string value)
        {
            value ??= string.Empty;
            canonical.Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(value);
            canonical.Append('\n');
        }
    }
}
