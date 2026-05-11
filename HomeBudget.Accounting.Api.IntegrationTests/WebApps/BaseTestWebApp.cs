using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NUnit.Framework;
using RestSharp;
using Serilog;

using HomeBudget.Accounting.Api.IntegrationTests.Constants;
using HomeBudget.Accounting.Api.IntegrationTests.Extensions;
using HomeBudget.Accounting.Api.IntegrationTests.Models;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Accounting.Infrastructure.Services.Interfaces;
using HomeBudget.Core.Constants;
using HomeBudget.Test.Core;

namespace HomeBudget.Accounting.Api.IntegrationTests.WebApps
{
    internal abstract class BaseTestWebApp<TWebAppEntryPoint, TWorkerEntryPoint> : BaseTestWebAppDispose
        where TWebAppEntryPoint : class
        where TWorkerEntryPoint : class
    {
        private static readonly object ActiveAppsLock = new();
        private static readonly List<BaseTestWebApp<TWebAppEntryPoint, TWorkerEntryPoint>> ActiveApps = [];

        private bool _hostsStopped;

        private IntegrationTestWebApplicationFactory<TWebAppEntryPoint> WebFactory { get; set; }

        private List<IntegrationTestWorkerFactory<TWorkerEntryPoint>> WorkerFactories { get; set; } = new List<IntegrationTestWorkerFactory<TWorkerEntryPoint>>();

        public bool ShouldInitializeWebApp { get; protected set; } = true;
        public bool ShouldInitializeWorkers { get; protected set; } = true;

        internal static TestContainersService TestContainersService { get; set; }

        internal RestClient RestHttpClient { get; set; }
        internal RestClient RestHttpClientAllowingHttpErrors { get; set; }

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

                TestContainersService = await TestContainersService.InitAsync();
                await StartContainersAsync();

                var kafkaContainerConnection = await TestContainersService.KafkaContainer.GetReachableBootstrapAsync();

                if (ShouldInitializeWorkers)
                {
                    for (var i = 0; i < workersMaxAmount; i++)
                    {
                        var worker = new IntegrationTestWorkerFactory<TWorkerEntryPoint>(
                            () => new TestContainersConnections
                            {
                                KafkaContainer = kafkaContainerConnection,
                                EventSourceDbContainer = TestContainersService.EventSourceDbContainer.GetConnectionString(),
                                MongoDbContainer = TestContainersService.MongoDbContainer.GetConnectionString(),
                                MsSqlDbContainer = TestContainersService.AccountingDbConnectionString,
                            });

                        WorkerFactories.Add(worker);
                    }

                    await Task.WhenAll(WorkerFactories.Select(w => w.StartAsync()));
                    await WaitForPaymentWorkerReadyAsync();
                }

                if (ShouldInitializeWebApp)
                {
                    WebFactory = new IntegrationTestWebApplicationFactory<TWebAppEntryPoint>(
                        () => new TestContainersConnections
                        {
                            KafkaContainer = kafkaContainerConnection,
                            EventSourceDbContainer = TestContainersService.EventSourceDbContainer.GetConnectionString(),
                            MongoDbContainer = TestContainersService.MongoDbContainer.GetConnectionString(),
                            MsSqlDbContainer = TestContainersService.AccountingDbConnectionString,
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

                    RestHttpClient = new RestClient(
                        baseClient,
                        new RestClientOptions
                        {
                            ThrowOnAnyError = true,
                            ConfigureMessageHandler = (handler) => new ErrorHandlerDelegatingHandler(new HttpClientHandler())
                        }
                    );

                    RestHttpClientAllowingHttpErrors = new RestClient(
                        baseClient,
                        new RestClientOptions
                        {
                            ThrowOnAnyError = false
                        }
                    );
                }

                RegisterActiveApp();

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                throw;
            }
        }

        private static async Task<bool> StartContainersAsync()
        {
            if (TestContainersService is null)
            {
                return false;
            }

            return TestContainersService.IsReadyForUse;
        }

        private void RegisterActiveApp()
        {
            lock (ActiveAppsLock)
            {
                if (!ActiveApps.Contains(this))
                {
                    ActiveApps.Add(this);
                }
            }
        }

        private static async Task StopActiveAppsAsync()
        {
            BaseTestWebApp<TWebAppEntryPoint, TWorkerEntryPoint>[] apps;

            lock (ActiveAppsLock)
            {
                apps = ActiveApps.ToArray();
                ActiveApps.Clear();
            }

            await Task.WhenAll(apps.Select(app => app.StopHostsAsync()));
        }

        private async Task StopHostsAsync()
        {
            if (_hostsStopped)
            {
                return;
            }

            RestHttpClient?.Dispose();
            RestHttpClientAllowingHttpErrors?.Dispose();

            if (WebFactory != null)
            {
                await WebFactory.DisposeAsync();
            }

            if (WorkerFactories is not null)
            {
                await Task.WhenAll(WorkerFactories.Select(w => w.StopAsync()));
            }

            _hostsStopped = true;
        }

        private async Task WaitForPaymentWorkerReadyAsync()
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(60);
            Exception lastException = null;

            while (DateTime.UtcNow < timeoutAt)
            {
                try
                {
                    foreach (var workerFactory in WorkerFactories)
                    {
                        var topicManager = workerFactory.WorkerHost?.Services.GetService<ITopicManager>();
                        if (topicManager is null)
                        {
                            continue;
                        }

                        if (await topicManager.HasActiveConsumerAsync(BaseTopics.AccountingPayments, "accounting.payments.group"))
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            throw new TimeoutException(
                $"Kafka payment worker did not join consumer group 'accounting.payments.group' for topic '{BaseTopics.AccountingPayments}' within 60 seconds. Last exception: {lastException?.Message}");
        }

        public static async Task ResetAsync()
        {
            if (TestContainersService == null)
            {
                return;
            }

            await StopActiveAppsAsync();
            await TestContainersService.ResetContainersAsync();
        }

        protected override async ValueTask DisposeAsyncCoreAsync()
        {
            await StopHostsAsync();

            if (TestContainersService != null)
            {
                await TestContainersService.DisposeAsync();
            }
        }
    }
}
