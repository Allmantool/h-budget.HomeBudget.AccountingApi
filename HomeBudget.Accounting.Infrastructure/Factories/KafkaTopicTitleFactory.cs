using System;

namespace HomeBudget.Accounting.Infrastructure.Factories
{
    public static class KafkaTopicTitleFactory
    {
        public static string GetPaymentAccountTopic(Guid paymentAccountId) => $"payment-account-{paymentAccountId}";
    }
}
