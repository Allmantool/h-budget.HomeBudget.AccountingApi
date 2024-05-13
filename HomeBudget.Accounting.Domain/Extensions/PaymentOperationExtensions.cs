using HomeBudget.Accounting.Domain.Models;

namespace HomeBudget.Accounting.Domain.Extensions
{
    public static class PaymentOperationExtensions
    {
        public static string GetIdentifier(this FinancialTransaction operation) => $"{operation.PaymentAccountId}-{operation.Key}";
    }
}
