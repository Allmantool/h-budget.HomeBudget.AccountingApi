using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

using EventStore.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using HomeBudget.Accounting.Api.IntegrationTests.Models;
using HomeBudget.Accounting.Domain.Constants;
using HomeBudget.Core.Models;
using HomeBudget.Core.Options;

namespace HomeBudget.Accounting.Api.IntegrationTests
{
    public class IntegrationTestWebApplicationFactory<TStartup>
    : WebApplicationFactory<TStartup>
        where TStartup : class
    {
        private readonly Func<TestContainersConnections> _webHostInitializationCallback;
        private TestContainersConnections _containersConnections;

        internal IConfiguration Configuration { get; private set; }

        public IntegrationTestWebApplicationFactory(
            Func<TestContainersConnections> webHostInitializationCallback)
        {
            _webHostInitializationCallback = webHostInitializationCallback;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = builder.Build();
            host.Start();

            return host;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var port = GetSecureRandomPort(5000, 6000);

            builder.UseKestrel(options =>
            {
                options.ListenLocalhost(port);
            });

            builder.UseUrls(
                $"http://localhost:{port}",
                $"http://127.0.0.1:{port}");

            builder.UseSetting(WebHostDefaults.ApplicationKey, typeof(TStartup).Assembly.FullName);

            builder.ConfigureAppConfiguration((_, conf) =>
            {
                conf.AddJsonFile(Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"appsettings.{HostEnvironments.Integration}.json"));

                conf.AddEnvironmentVariables();

                _containersConnections = _webHostInitializationCallback.Invoke();

                conf.AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Kafka:BootstrapServers"] = _containersConnections.KafkaContainer,
                    ["MongoDb:ConnectionString"] = _containersConnections.MongoDbContainer,
                    ["EventStore:ConnectionString"] = _containersConnections.EventSourceDbContainer
                });

                Configuration = conf.Build();
            });

            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddFilter("Grpc.Net.Client.Internal", LogLevel.Warning);
            });

            builder.ConfigureServices(services =>
            {
                var kafkaOptions = new KafkaOptions
                {
                    ProducerSettings = new ProducerSettings
                    {
                        BootstrapServers = _containersConnections?.KafkaContainer,
                    },
                    ConsumerSettings = new ConsumerSettings
                    {
                        BootstrapServers = _containersConnections?.KafkaContainer
                    },
                    AdminSettings = new AdminSettings
                    {
                        BootstrapServers = _containersConnections?.KafkaContainer
                    }
                };

                services.Configure<KafkaOptions>(opts =>
                {
                    opts.AdminSettings = kafkaOptions.AdminSettings;
                    opts.ProducerSettings = kafkaOptions.ProducerSettings;
                    opts.ConsumerSettings = kafkaOptions.ConsumerSettings;
                });

                services.Configure<MongoDbOptions>(opts =>
                {
                    opts.ConnectionString = _containersConnections?.MongoDbContainer;
                    opts.PaymentsHistory = "payments_history_test";
                    opts.HandBooks = "handbooks_test";
                    opts.PaymentAccounts = "payment_accounts_test";
                });

                var eventStoreDbOptions = new EventStoreDbOptions();
                var eventStoreDbSettings = EventStoreClientSettings.Create(_containersConnections.EventSourceDbContainer);
                eventStoreDbSettings.OperationOptions = new EventStoreClientOperationOptions
                {
                    ThrowOnAppendFailure = true
                };
                eventStoreDbSettings.DefaultDeadline = TimeSpan.FromSeconds(
                    eventStoreDbOptions.TimeoutInSeconds * (eventStoreDbOptions.RetryAttempts + 1));

                eventStoreDbSettings.ConnectivitySettings = new EventStoreClientConnectivitySettings
                {
                    Address = new Uri(_containersConnections.EventSourceDbContainer),
                    KeepAliveInterval = TimeSpan.FromSeconds(eventStoreDbOptions.KeepAliveInterval),
                    GossipTimeout = TimeSpan.FromSeconds(eventStoreDbOptions.GossipTimeout),
                    DiscoveryInterval = TimeSpan.FromSeconds(eventStoreDbOptions.DiscoveryInterval),
                    MaxDiscoverAttempts = eventStoreDbOptions.MaxDiscoverAttempts
                };

                services.AddEventStoreClient(
                    _containersConnections.EventSourceDbContainer,
                    settings =>
                    {
                        settings = eventStoreDbSettings;
                    });
            });
        }

        private static int GetSecureRandomPort(int minPort, int maxPort)
        {
            using var rng = RandomNumberGenerator.Create();
            var portRange = maxPort - minPort;
            var randomBytes = new byte[4];

            rng.GetBytes(randomBytes);
            var randomValue = BitConverter.ToUInt32(randomBytes, 0);

            return minPort + (int)(randomValue % portRange);
        }
    }
}
