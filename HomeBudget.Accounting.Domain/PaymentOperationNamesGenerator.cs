using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Domain
{
    public static class PaymentOperationNamesGenerator
    {
        public static string GenerateForAccountMonthStream(string baseStreamName)
            => string.Join(
                NameConventions.EventPrefixSeparator,
                EventDbEventStreams.PaymentAccountPrefix,
                baseStreamName);
    }
}
