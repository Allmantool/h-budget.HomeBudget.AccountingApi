using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Core.Constants;

namespace HomeBudget.Accounting.Domain.Extensions
{
    public static class PaymentOperationExtensions
    {
        public static string GetPaymentAccountIdentifier(this FinancialTransaction operation) => $"{operation.PaymentAccountId}-{operation.Key}";

        public static string GetMonthPeriodPaymentAccountIdentifier(this FinancialTransaction operation)
        {
            var paymentPeriod = operation.OperationDay.ToFinancialPeriod();
            var startPeriod = paymentPeriod.StartDate.ToString(DateTimeFormats.FinancialPeriod);
            var endPeriod = paymentPeriod.EndDate.ToString(DateTimeFormats.FinancialPeriod);

            return $"{operation.PaymentAccountId}-{startPeriod}-{endPeriod}";
        }
    }
}
