using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Components.Operations.Models;

namespace HomeBudget.Components.Operations
{
    internal static class PaymentOperationNamesGenerator
    {
        public static string GetEventSteamName(string paymentAccountId)
            => string.Join(
                NameConventions.EventPrefixSeparator,
                new[] { paymentAccountId, nameof(PaymentOperationEvent) });
    }
}
