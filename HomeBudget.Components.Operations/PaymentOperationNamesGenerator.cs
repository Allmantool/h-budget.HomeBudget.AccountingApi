using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Components.Operations
{
    internal static class PaymentOperationNamesGenerator
    {
        public static string GenerateForAccountMonthStream(string monthPeriodIdentifier)
            => string.Join(
                NameConventions.EventPrefixSeparator,
                nameof(PaymentAccount),
                monthPeriodIdentifier);
    }
}
