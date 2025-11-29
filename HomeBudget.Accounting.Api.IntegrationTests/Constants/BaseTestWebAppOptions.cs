namespace HomeBudget.Accounting.Api.IntegrationTests.Constants
{
    internal record BaseTestWebAppOptions
    {
        public static int WebClientTimeoutInMinutes { get; set; } = 2;
    }
}
