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
    internal abstract class BaseTestWebApp<TEntryPoint> : BaseTestWebAppDispose
        where TEntryPoint : class
    {
        private readonly HttpClient _client;
        private IntegrationTestWebApplicationFactory<TEntryPoint> WebFactory { get; }
        private TestContainersService TestContainersService { get; set; }

        internal RestClient RestHttpClient { get; }

        protected BaseTestWebApp()
        {
            try
            {
                BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
                BsonSerializer.TryRegisterSerializer(new DateOnlySerializer());

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

                        var isStarted = StartAsync().GetAwaiter().GetResult();

                        return new TestContainersConnections
                        {
                            KafkaContainer = TestContainersService.KafkaContainer.GetBootstrapAddress(),
                            EventSourceDbContainer = TestContainersService.EventSourceDbContainer.GetConnectionString(),
                            MongoDbContainer = TestContainersService.MongoDbContainer.GetConnectionString()
                        };
                    });

                var clientBaseUrl = new Uri("http://localhost:6064");
                var clientOptions = new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                    BaseAddress = clientBaseUrl,
                    HandleCookies = true,
                    MaxAutomaticRedirections = 7
                };

                var handler = new ErrorHandlerDelegatingHandler(new HttpClientHandler());
                var baseClient = WebFactory.CreateDefaultClient(clientOptions.BaseAddress, handler);
                var restClientOptions = new RestClientOptions(baseClient.BaseAddress)
                {
                    ThrowOnAnyError = true
                };

                _client = new HttpClient(handler)
                {
                    BaseAddress = baseClient.BaseAddress,
                    Timeout = baseClient.Timeout
                };

                RestHttpClient = new RestClient(
                    _client,
                    restClientOptions
                );
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public async Task<bool> StartAsync()
        {
            if (TestContainersService is null)
            {
                return false;
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

            _client?.Dispose();
            RestHttpClient?.Dispose();
        }
    }
}
