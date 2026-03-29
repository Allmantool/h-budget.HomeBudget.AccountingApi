namespace HomeBudget.Core.Constants
{
    public static class EventMetadataKeys
    {
        public static readonly string CorrelationId = nameof(CorrelationId);

        public static readonly string Version = nameof(Version);

        public static readonly string FromMessage = nameof(FromMessage);

        public static readonly string ExceptionDetails = nameof(ExceptionDetails);

        public static readonly string TraceId = "trace-id";

        public static readonly string TraceParent = "trace-parent";

        public static readonly string TraceState = "trace-state";

        public static readonly string Baggage = "baggage";

        public static readonly string MessageId = "message-id";

        public static readonly string CausationId = "causation-id";
    }
}
