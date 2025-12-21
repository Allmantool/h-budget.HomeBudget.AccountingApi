namespace HomeBudget.Accounting.Domain.Constants
{
    public static class EventDbEventStreams
    {
        public static readonly string DeadLetter = "dead-letter-stream";

        public static readonly string PaymentAccountPrefix = "payment-account";
    }
}
