using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NUnit.Framework;
using RestSharp;

using HomeBudget.Accounting.Api.Constants;
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
        private IntegrationTestWebApplicationFactory<TWebAppEntryPoint> WebFactory { get; set; }

        private List<IntegrationTestWorkerFactory<TWorkerEntryPoint>> WorkerFactories { get; set; } = new List<IntegrationTestWorkerFactory<TWorkerEntryPoint>>();

        internal static TestContainersService TestContainersService { get; } = new TestContainersService();

        internal RestClient RestHttpClient { get; set; }

        public async Task<bool> InitAsync(int workersMaxAmount = 1)
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

                await StartContainersAsync();

                for (var i = 0; i < workersMaxAmount; i++)
                {
                    var worker = new IntegrationTestWorkerFactory<TWorkerEntryPoint>(
                        () => new TestContainersConnections
                        {
                            KafkaContainer = TestContainersService.KafkaContainer.GetBootstrapAddress(),
                            EventSourceDbContainer = TestContainersService.EventSourceDbContainer.GetConnectionString(),
                            MongoDbContainer = TestContainersService.MongoDbContainer.GetConnectionString()
                        });

                    WorkerFactories.Add(worker);
                }

                await Task.WhenAll(WorkerFactories.Select(w => w.StartAsync()));

                WebFactory = new IntegrationTestWebApplicationFactory<TWebAppEntryPoint>(
                    () => new TestContainersConnections
                    {
                        KafkaContainer = TestContainersService.KafkaContainer.GetBootstrapAddress(),
                        EventSourceDbContainer = TestContainersService.EventSourceDbContainer.GetConnectionString(),
                        MongoDbContainer = TestContainersService.MongoDbContainer.GetConnectionString()
                    });

                var server = WebFactory.Server;
                var addresses = server.Features.Get<IServerAddressesFeature>();
                var realAddress = addresses.Addresses.FirstOrDefault();
                var baseAddress = realAddress is null ?
                    WebFactory.ClientOptions.BaseAddress
                    : new Uri(realAddress);

                var clientOptions = new WebApplicationFactoryClientOptions
                {
                    BaseAddress = baseAddress,
                    AllowAutoRedirect = true,
                    HandleCookies = true
                };

                var baseClient = WebFactory.CreateClient(clientOptions);
                baseClient.Timeout = TimeSpan.FromMinutes(BaseTestWebAppOptions.WebClientTimeoutInMinutes);

                var healthUri = new Uri(baseClient.BaseAddress!, Endpoints.HealthCheckSource);
                var response = await baseClient.GetAsync(healthUri);
                response.EnsureSuccessStatusCode();

                RestHttpClient = new RestClient(
                    baseClient,
                    new RestClientOptions()
                    {
                        ThrowOnAnyError = true,
                        ConfigureMessageHandler = (handler) => new ErrorHandlerDelegatingHandler(new HttpClientHandler())
                    }
                );

                return true;
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public static async Task<bool> StartContainersAsync()
        {
            if (TestContainersService is null)
            {
                return false;
            }

            return await TestContainersService.UpAndRunningContainersAsync();
        }

        public static async Task ResetAsync()
        {
            if (TestContainersService == null)
            {
                return;
            }

            await TestContainersService.ResetContainersAsync();
        }

        public static async Task StopAsync()
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

            if (WorkerFactories is not null)
            {
                await Task.WhenAll(WorkerFactories.Select(w => w.StopAsync()));
            }

            RestHttpClient?.Dispose();
        }
    }
}
