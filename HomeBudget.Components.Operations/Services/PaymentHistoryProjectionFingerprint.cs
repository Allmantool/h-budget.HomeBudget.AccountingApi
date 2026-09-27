using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations.Services
{
    internal static class PaymentHistoryProjectionFingerprint
    {
        public static string Create(IEnumerable<PaymentOperationHistoryRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);

            var canonical = new StringBuilder();
            foreach (var item in records
                .OrderBy(static record => record.StreamRevision)
                .ThenBy(static record => record.Record.OperationDay)
                .ThenBy(static record => record.Record.OperationUnixTime)
                .ThenBy(static record => record.Record.Key))
            {
                Append(canonical, item.StreamRevision?.ToString(CultureInfo.InvariantCulture));
                Append(canonical, item.Balance.ToString(CultureInfo.InvariantCulture));
                Append(canonical, item.Record.Key.ToString("D"));
                Append(canonical, item.Record.TransactionType?.Key.ToString(CultureInfo.InvariantCulture));
                Append(canonical, item.Record.OperationDay.ToString("O", CultureInfo.InvariantCulture));
                Append(canonical, item.Record.OperationUnixTime.ToString(CultureInfo.InvariantCulture));
                Append(canonical, item.Record.Comment);
                Append(canonical, item.Record.ContractorId.ToString("D"));
                Append(canonical, item.Record.CategoryId.ToString("D"));
                Append(canonical, item.Record.PaymentAccountId.ToString("D"));
                Append(canonical, item.Record.ConversionMultiplier?.ToString(CultureInfo.InvariantCulture));
                Append(canonical, item.Record.Amount.ToString(CultureInfo.InvariantCulture));
                Append(canonical, item.Record.ScopedOperationId.ToString(CultureInfo.InvariantCulture));
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        }

        private static void Append(StringBuilder builder, string value)
        {
            value ??= string.Empty;
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }
    }
}
