namespace HomeBudget.Accounting.Api.Constants
{
    internal static class Endpoints
    {
        public const string PaymentOperations = "payment-operations";
        public const string PaymentsHistory = "payments-history";

        public const string PaymentOperationsByPaymentAccountId = "payment-operations/{paymentAccountId}";
        public const string PaymentsHistoryByPaymentAccountId = "payments-history/{paymentAccountId}";
        public const string PaymentAccounts = "payment-accounts";
        public const string Contractors = "contractors";
        public const string Categories = "categories";

        public const string HealthCheckSource = "/health";
    }
}
