using System;
using System.Threading.Tasks;

using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal abstract class BaseTestWebApp<TEntryPoint> : BaseTestWebAppDispose
           where TEntryPoint : class
    {
        private IntegrationTestWebApplicationFactory<TEntryPoint> WebFactory { get; }

        internal RestClient RestHttpClient { get; }

        protected BaseTestWebApp()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", HostEnvironments.Integration);

            var testProperties = TestContext.CurrentContext.Test.Properties;
            var testCategory = testProperties.Get("Category") as string;

            if (TestTypes.Integration.Equals(testCategory, StringComparison.OrdinalIgnoreCase))
            {
                WebFactory = new IntegrationTestWebApplicationFactory<TEntryPoint>();

                RestHttpClient = new RestClient(
                    WebFactory.CreateClient(),
                    new RestClientOptions(new Uri("http://localhost:6064")));
            }
        }

        protected override async ValueTask DisposeAsyncCoreAsync()
        {
            if (WebFactory != null)
            {
                await WebFactory.DisposeAsync();
            }

            RestHttpClient?.Dispose();
        }
    }
}
