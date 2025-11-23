namespace HomeBudget.Accounting.Api.IntegrationTests.Constants
{
    internal static class BaseTestContainerOptions
    {
        public static int StopTimeoutInMinutes { get; set; } = 30;

        public static long NanoCPUs { get; set; } = 1500000000;

        public static long Memory { get; set; } = 1024 * 1024 * 1024;
    }
}
