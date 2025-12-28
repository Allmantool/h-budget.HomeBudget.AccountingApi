using System;

using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Extensions
{
    public static class FinancialTransactionExtensions
    {
        public static string GetPartitionKey(this FinancialTransaction paylod)
        {
            ArgumentNullException.ThrowIfNull(paylod);

            return $"{paylod.PaymentAccountId}-{paylod.Key}";
        }
    }
}
