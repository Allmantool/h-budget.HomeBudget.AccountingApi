namespace HomeBudget.Accounting.Api.IntegrationTests.Constants
{
    internal static class BaseTestContainerOptions
    {
        public static int WaitStrategyInSeconds { get; set; } = 30;
        public static int StopTimeoutInMinutes { get; set; } = 30;
        public static long NanoCPUs { get; set; } = 1_500_000_000;
        public static long Memory { get; set; } = 1024 * 1024 * 1024;
    }
}
