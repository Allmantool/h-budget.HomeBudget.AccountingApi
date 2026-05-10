using HomeBudget.Accounting.Domain.Models;
namespace HomeBudget.Accounting.Domain.Extensions
{
    public static class PaymentOperationExtensions
    {
        public static string GetPaymentAccountIdentifier(this FinancialTransaction operation) => $"{operation.PaymentAccountId}-{operation.Key}";

        public static string GetMonthPeriodPaymentAccountIdentifier(this FinancialTransaction operation)
        {
            var paymentPeriod = operation.OperationDay.ToFinancialPeriod();

            return paymentPeriod.ToFinancialMonthIdentifier(operation.PaymentAccountId);
        }
    }
}
