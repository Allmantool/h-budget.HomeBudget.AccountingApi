﻿using System;
using System.Threading.Tasks;

using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Models;
using HomeBudget.Accounting.Domain.Constants;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal abstract class BaseTestWebApp<TEntryPoint> : BaseTestWebAppDispose
        where TEntryPoint : class
    {
        private IntegrationTestWebApplicationFactory<TEntryPoint> WebFactory { get; }
        private TestContainersService TestContainersService { get; set; }

        internal RestClient RestHttpClient { get; }

        protected BaseTestWebApp()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", HostEnvironments.Integration);

            var testProperties = TestContext.CurrentContext.Test.Properties;
            var testCategory = testProperties.Get("Category") as string;

            if (!TestTypes.Integration.Equals(testCategory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            WebFactory = new IntegrationTestWebApplicationFactory<TEntryPoint>(
                () =>
                {
                    TestContainersService = new TestContainersService(WebFactory?.Configuration);

                    StartAsync().GetAwaiter().GetResult();

                    return new TestContainersConnections
                    {
                        KafkaContainer = TestContainersService.KafkaContainer.GetBootstrapAddress(),
                        EventSourceDbContainer = TestContainersService.EventSourceDbContainer.GetConnectionString()
                    };
                });

            RestHttpClient = new RestClient(
                WebFactory.CreateClient(),
                new RestClientOptions(new Uri("http://localhost:6064")));
        }

        public async Task StartAsync()
        {
            await TestContainersService.UpAndRunningContainersAsync();
        }

        public async Task StopAsync()
        {
            await TestContainersService.StopAsync();
        }

        protected override async ValueTask DisposeAsyncCoreAsync()
        {
            if (TestContainersService != null)
            {
                await TestContainersService.DisposeAsync();
            }

            if (WebFactory != null)
            {
                await WebFactory.DisposeAsync();
            }

            RestHttpClient?.Dispose();
        }
    }
}
