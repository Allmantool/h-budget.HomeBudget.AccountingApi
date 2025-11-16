using System;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Models;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Test.Core;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal abstract class BaseTestWebApp<TWebAppEntryPoint, TWorkerEntryPoint> : BaseTestWebAppDispose
        where TWebAppEntryPoint : class
        where TWorkerEntryPoint : class
    {
        private HttpClient Client { get; set; }
        private IntegrationTestWebApplicationFactory<TWebAppEntryPoint> WebFactory { get; set; }

        private IntegrationTestWorkerFactory<TWorkerEntryPoint> WorkerFactory { get; set; }

        private TestContainersService TestContainersService { get; set; }

        internal RestClient RestHttpClient { get; set; }

        public async Task<bool> InitAsync()
        {
            try
            {
                BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
                BsonSerializer.TryRegisterSerializer(new DateOnlySerializer());

                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", HostEnvironments.Integration);
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", HostEnvironments.Integration);

                var testProperties = TestContext.CurrentContext.Test.Properties;
                var testCategory = testProperties.Get("Category") as string;

                if (!TestTypes.Integration.Equals(testCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                TestContainersService = new TestContainersService();

                await StartContainersAsync();

                WorkerFactory = new IntegrationTestWorkerFactory<TWorkerEntryPoint>(
                    () => new TestContainersConnections
                    {
                        KafkaContainer = TestContainersService.KafkaContainer.GetBootstrapAddress(),
                        EventSourceDbContainer = TestContainersService.EventSourceDbContainer.GetConnectionString(),
                        MongoDbContainer = TestContainersService.MongoDbContainer.GetConnectionString()
                    });

                await WorkerFactory.StartAsync();

                WebFactory = new IntegrationTestWebApplicationFactory<TWebAppEntryPoint>(
                    () => new TestContainersConnections
                    {
                        KafkaContainer = TestContainersService.KafkaContainer.GetBootstrapAddress(),
                        EventSourceDbContainer = TestContainersService.EventSourceDbContainer.GetConnectionString(),
                        MongoDbContainer = TestContainersService.MongoDbContainer.GetConnectionString()
                    });

                var clientOptions = new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = true,
                    HandleCookies = true
                };

                var handler = new ErrorHandlerDelegatingHandler(new HttpClientHandler());
                var baseClient = WebFactory.CreateClient(clientOptions);

                var restClientOptions = new RestClientOptions(baseClient.BaseAddress)
                {
                    ThrowOnAnyError = true
                };

                Client = new HttpClient(handler)
                {
                    BaseAddress = baseClient.BaseAddress,
                    Timeout = TimeSpan.FromMinutes(2)
                };

                RestHttpClient = new RestClient(
                    Client,
                    restClientOptions
                );

                return true;
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public async Task<bool> StartContainersAsync()
        {
            if (TestContainersService is null)
            {
                return false;
            }

            if (TestContainersService.IsStarted)
            {
                return true;
            }

            return await TestContainersService.UpAndRunningContainersAsync();
        }

        public async Task ResetAsync()
        {
            if (TestContainersService == null)
            {
                return;
            }

            await TestContainersService.ResetContainersAsync();
        }

        public async Task StopAsync()
        {
            if (TestContainersService == null)
            {
                return;
            }

            await TestContainersService.StopAsync();
        }

        protected override async ValueTask DisposeAsyncCoreAsync()
        {
            if (TestContainersService != null)
            {
                await TestContainersService.StopAsync();
                await TestContainersService.DisposeAsync();
            }

            if (WebFactory != null)
            {
                await WebFactory.DisposeAsync();
            }

            if (WorkerFactory is not null)
            {
                _ = WorkerFactory.StopAsync();
            }

            Client?.Dispose();
            RestHttpClient?.Dispose();
        }
    }
}
