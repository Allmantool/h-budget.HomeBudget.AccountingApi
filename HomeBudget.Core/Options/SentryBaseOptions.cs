namespace HomeBudget.Core.Options
{
    public static class SentryBaseOptions
    {
        public static readonly double TracesSampleRateForDevelopment = 1.0;
        public static readonly double TracesSampleRateForProduction = 0.3;

        public static readonly string UriConfigurationKey = "Sentry:Dsn";
    }
}
