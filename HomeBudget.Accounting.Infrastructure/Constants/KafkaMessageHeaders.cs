namespace HomeBudget.Accounting.Infrastructure.Constants
{
    public static class KafkaMessageHeaders
    {
        public static readonly string Type = "message-type";
        public static readonly string Version = "message-version";
        public static readonly string Source = "source";
        public static readonly string EnvelopId = "envelop-Id";
        public static readonly string OccuredOn = "occured-on";
        public static readonly string ProcessedAt = "processed-at";
        public static readonly string CorrelationId = "correlation-id";
        public static readonly string MessageId = "message-id";
        public static readonly string CausationId = "causation-id";

        // public static readonly string CorrelationId = "correlationId";
        public static readonly string Traceparent = "traceparent";
        public static readonly string Tracestate = "tracestate";
        public static readonly string Baggage = "baggage";
        public static readonly string TraceId = "traceId";
    }
}
